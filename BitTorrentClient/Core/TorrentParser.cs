using System.Text;
using System.Text.Json;
using System.IO;
using System.Collections.Generic;

namespace BitTorrentClient.Core;

public class TorrentInfo
{
    public string Name { get; set; } = string.Empty;
    public long PieceLength { get; set; }
    public string Pieces { get; set; } = string.Empty;
    public bool IsPrivate { get; set; }
    public List<TorrentFile> Files { get; set; } = new();
    public long TotalSize { get; set; }
    public string InfoHash { get; set; } = string.Empty;
}

public class TorrentFile
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public long Offset { get; set; }
}

public class TorrentMetadata
{
    public string Announce { get; set; } = string.Empty;
    public List<string> AnnounceList { get; set; } = new();
    public string Comment { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public long CreationDate { get; set; }
    public string Encoding { get; set; } = string.Empty;
    public TorrentInfo Info { get; set; } = new();
}

public class TorrentParser
{
    public static TorrentMetadata ParseTorrentFile(string filePath)
    {
        var fileBytes = File.ReadAllBytes(filePath);
        var infoStart = FindInfoStart(fileBytes);
        var infoEnd = FindInfoEnd(fileBytes, infoStart);
        var infoBytes = fileBytes.Skip(infoStart).Take(infoEnd - infoStart).ToArray();

        var decoded = BencodeParser.Decode(fileBytes);
        if (decoded is not Dictionary<string, object> dict)
            throw new ArgumentException("Invalid torrent file format");

        var metadata = new TorrentMetadata();

        // Parse announce
        if (dict.TryGetValue("announce", out var announce) && announce is string announceStr)
            metadata.Announce = announceStr;

        // Parse announce-list
        if (dict.TryGetValue("announce-list", out var announceList) && announceList is List<object> announceListObj)
        {
            foreach (var tier in announceListObj)
            {
                if (tier is List<object> tierList)
                {
                    foreach (var url in tierList)
                    {
                        if (url is string urlStr)
                            metadata.AnnounceList.Add(urlStr);
                    }
                }
            }
        }

        // Parse other metadata
        if (dict.TryGetValue("comment", out var comment) && comment is string commentStr)
            metadata.Comment = commentStr;

        if (dict.TryGetValue("created by", out var createdBy) && createdBy is string createdByStr)
            metadata.CreatedBy = createdByStr;

        if (dict.TryGetValue("creation date", out var creationDate) && creationDate is long creationDateLong)
            metadata.CreationDate = creationDateLong;

        if (dict.TryGetValue("encoding", out var encoding) && encoding is string encodingStr)
            metadata.Encoding = encodingStr;

        // Parse info dictionary
        if (dict.TryGetValue("info", out var info) && info is Dictionary<string, object> infoDict)
        {
            metadata.Info = ParseInfoDictionary(infoDict);
            // Use raw infoBytes for info hash
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hashBytes = sha1.ComputeHash(infoBytes);
            metadata.Info.InfoHash = Convert.ToHexString(hashBytes).ToLower();
        }

        return metadata;
    }

    // Helper to find the start of the 'info' dictionary in the bencoded file
    private static int FindInfoStart(byte[] fileBytes)
    {
        var infoKey = Encoding.ASCII.GetBytes("4:info");
        for (int i = 0; i < fileBytes.Length - infoKey.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < infoKey.Length; j++)
            {
                if (fileBytes[i + j] != infoKey[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                // The info dict starts right after '4:info'
                return i + infoKey.Length;
            }
        }
        throw new Exception("Could not find 'info' dictionary in torrent file.");
    }

    // Helper to find the end of the 'info' dictionary in the bencoded file
    private static int FindInfoEnd(byte[] fileBytes, int infoStart)
    {
        // Use a stack to track nested dictionaries/lists for robust parsing
        var stack = new Stack<byte>();
        int i = infoStart;
        if (fileBytes[i] != (byte)'d')
            throw new Exception("'info' dictionary does not start with 'd'.");
        stack.Push((byte)'d');
        i++;
        while (i < fileBytes.Length)
        {
            byte b = fileBytes[i];
            if (b == (byte)'d' || b == (byte)'l')
            {
                stack.Push(b);
                i++;
            }
            else if (b == (byte)'e')
            {
                stack.Pop();
                i++;
                if (stack.Count == 0)
                    return i;
            }
            else if (b == (byte)'i')
            {
                // Skip integer: i<number>e
                i++;
                while (fileBytes[i] != (byte)'e') i++;
                i++; // skip 'e'
            }
            else if (b >= (byte)'0' && b <= (byte)'9')
            {
                // Parse string length and skip string
                int lenStart = i;
                while (fileBytes[i] != (byte)':') i++;
                var lenStr = Encoding.ASCII.GetString(fileBytes, lenStart, i - lenStart);
                if (!long.TryParse(lenStr, out long len) || len < 0)
                    throw new Exception($"Invalid string length in bencode: '{lenStr}'");
                i++; // skip ':'
                i += (int)len;
            }
            else
            {
                throw new Exception($"Unexpected byte in bencode at position {i}: {b}");
            }
        }
        throw new Exception("Could not find end of 'info' dictionary in torrent file.");
    }

    private static TorrentInfo ParseInfoDictionary(Dictionary<string, object> infoDict)
    {
        var info = new TorrentInfo();

        // Parse name
        if (infoDict.TryGetValue("name", out var name) && name is string nameStr)
            info.Name = nameStr;

        // Parse piece length
        if (infoDict.TryGetValue("piece length", out var pieceLength) && pieceLength is long pieceLengthLong)
            info.PieceLength = pieceLengthLong;

        // Parse pieces
        if (infoDict.TryGetValue("pieces", out var pieces) && pieces is byte[] piecesBytes)
            info.Pieces = Convert.ToBase64String(piecesBytes);

        // Parse private flag
        if (infoDict.TryGetValue("private", out var isPrivate) && isPrivate is long isPrivateLong)
            info.IsPrivate = isPrivateLong == 1;

        // Parse files
        if (infoDict.TryGetValue("files", out var files) && files is List<object> filesList)
        {
            // Multi-file torrent
            long offset = 0;
            foreach (var fileObj in filesList)
            {
                if (fileObj is Dictionary<string, object> fileDict)
                {
                    var file = new TorrentFile { Offset = offset };

                    if (fileDict.TryGetValue("length", out var length) && length is long lengthLong)
                        file.Length = lengthLong;

                    if (fileDict.TryGetValue("path", out var path) && path is List<object> pathList)
                    {
                        var pathParts = new List<string>();
                        foreach (var pathPart in pathList)
                        {
                            if (pathPart is string pathPartStr)
                                pathParts.Add(pathPartStr);
                        }
                        file.Path = Path.Combine(pathParts.ToArray());
                    }

                    info.Files.Add(file);
                    offset += file.Length;
                }
            }
        }
        else if (infoDict.TryGetValue("length", out var length) && length is long lengthLong)
        {
            // Single file torrent
            info.Files.Add(new TorrentFile
            {
                Path = info.Name,
                Length = lengthLong,
                Offset = 0
            });
        }

        info.TotalSize = info.Files.Sum(f => f.Length);
        return info;
    }
}

public static class BencodeParser
{
    public static object Decode(byte[] data)
    {
        var position = 0;
        return DecodeValue(data, ref position);
    }

    private static object DecodeValue(byte[] data, ref int position)
    {
        var currentByte = data[position];

        if (currentByte == 'i') // Integer
        {
            return DecodeInteger(data, ref position);
        }
        else if (currentByte == 'l') // List
        {
            return DecodeList(data, ref position);
        }
        else if (currentByte == 'd') // Dictionary
        {
            return DecodeDictionary(data, ref position);
        }
        else if (char.IsDigit((char)currentByte)) // String
        {
            return DecodeString(data, ref position);
        }

        throw new ArgumentException($"Unknown bencode type: {(char)currentByte}");
    }

    private static long DecodeInteger(byte[] data, ref int position)
    {
        position++; // Skip 'i'
        var start = position;
        
        while (position < data.Length && data[position] != 'e')
            position++;

        var numberStr = Encoding.ASCII.GetString(data, start, position - start);
        position++; // Skip 'e'
        
        return long.Parse(numberStr);
    }

    private static string DecodeString(byte[] data, ref int position)
    {
        var start = position;
        
        while (position < data.Length && data[position] != ':')
            position++;

        var lengthStr = Encoding.ASCII.GetString(data, start, position - start);
        var length = int.Parse(lengthStr);
        
        position++; // Skip ':'
        var stringData = Encoding.UTF8.GetString(data, position, length);
        position += length;
        
        return stringData;
    }

    private static List<object> DecodeList(byte[] data, ref int position)
    {
        position++; // Skip 'l'
        var list = new List<object>();

        while (position < data.Length && data[position] != 'e')
        {
            list.Add(DecodeValue(data, ref position));
        }

        position++; // Skip 'e'
        return list;
    }

    private static Dictionary<string, object> DecodeDictionary(byte[] data, ref int position)
    {
        position++; // Skip 'd'
        var dict = new Dictionary<string, object>();

        while (position < data.Length && data[position] != 'e')
        {
            var key = DecodeString(data, ref position);
            var valueStart = position;
            var value = DecodeValue(data, ref position);
            // Debug: log key and value type
            Console.WriteLine($"[DEBUG] Decoded key: {key}, value type: {value?.GetType()} at pos {valueStart}");
            // Special handling for 'peers' key: store as byte[] if value is string
            if (key == "peers" && value is string strVal)
            {
                var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(strVal);
                dict[key] = bytes;
            }
            else
            {
                dict[key] = value;
            }
        }

        position++; // Skip 'e'
        return dict;
    }

    public static byte[] Encode(Dictionary<string, object> dict)
    {
        var result = new List<byte>();
        result.Add((byte)'d');

        foreach (var kvp in dict.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            // Encode key as ASCII
            var keyBytes = Encoding.ASCII.GetBytes(kvp.Key);
            result.AddRange(Encoding.ASCII.GetBytes(keyBytes.Length.ToString()));
            result.Add((byte)':');
            result.AddRange(keyBytes);

            // Encode value
            result.AddRange(EncodeValue(kvp.Value));
        }

        result.Add((byte)'e');
        return result.ToArray();
    }

    private static byte[] EncodeValue(object value)
    {
        return value switch
        {
            string str => EncodeString(str),
            long l => EncodeInteger(l),
            int i => EncodeInteger(i),
            List<object> list => EncodeList(list),
            Dictionary<string, object> dict => Encode(dict),
            byte[] bytes => EncodeBytes(bytes),
            _ => throw new ArgumentException($"Unsupported type: {value.GetType()}")
        };
    }

    private static byte[] EncodeString(string str)
    {
        // Use ASCII for bencode strings
        var bytes = Encoding.ASCII.GetBytes(str);
        var result = new List<byte>();
        result.AddRange(Encoding.ASCII.GetBytes(bytes.Length.ToString()));
        result.Add((byte)':');
        result.AddRange(bytes);
        return result.ToArray();
    }

    private static byte[] EncodeInteger(long value)
    {
        var result = new List<byte>();
        result.Add((byte)'i');
        result.AddRange(Encoding.ASCII.GetBytes(value.ToString()));
        result.Add((byte)'e');
        return result.ToArray();
    }

    private static byte[] EncodeList(List<object> list)
    {
        var result = new List<byte>();
        result.Add((byte)'l');

        foreach (var item in list)
        {
            result.AddRange(EncodeValue(item));
        }

        result.Add((byte)'e');
        return result.ToArray();
    }

    private static byte[] EncodeBytes(byte[] bytes)
    {
        var result = new List<byte>();
        result.AddRange(Encoding.ASCII.GetBytes(bytes.Length.ToString()));
        result.Add((byte)':');
        result.AddRange(bytes);
        return result.ToArray();
    }
} 