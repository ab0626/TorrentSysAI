using System.Security.Cryptography;
using System.Collections;
using System.IO;

namespace BitTorrentClient.Core;

public class Piece
{
    public int Index { get; set; }
    public long Offset { get; set; }
    public int Length { get; set; }
    public string Hash { get; set; } = string.Empty;
    public bool IsDownloaded { get; set; }
    public bool IsVerified { get; set; }
    public byte[]? Data { get; set; }
}

public class PieceManager
{
    private readonly TorrentInfo _torrentInfo;
    private readonly string _downloadPath;
    private readonly List<Piece> _pieces;
    private readonly BitArray _downloadedPieces;
    private readonly object _lockObject = new();
    private readonly Dictionary<int, List<PieceRequest>> _pendingRequests = new();

    public event EventHandler<int>? PieceDownloaded;
    public event EventHandler<int>? PieceVerified;
    public event EventHandler<double>? ProgressChanged;

    public PieceManager(TorrentInfo torrentInfo, string downloadPath)
    {
        _torrentInfo = torrentInfo;
        _downloadPath = downloadPath;
        _pieces = new List<Piece>();
        _downloadedPieces = new BitArray(GetTotalPieces());
        
        InitializePieces();
    }

    private void InitializePieces()
    {
        var pieceHashes = GetPieceHashes();
        var totalPieces = pieceHashes.Count;
        
        for (int i = 0; i < totalPieces; i++)
        {
            var offset = (long)i * _torrentInfo.PieceLength;
            var length = (int)Math.Min(_torrentInfo.PieceLength, _torrentInfo.TotalSize - offset);
            
            _pieces.Add(new Piece
            {
                Index = i,
                Offset = offset,
                Length = length,
                Hash = pieceHashes[i]
            });
        }
    }

    private List<string> GetPieceHashes()
    {
        var piecesBytes = Convert.FromBase64String(_torrentInfo.Pieces);
        var hashes = new List<string>();
        
        for (int i = 0; i < piecesBytes.Length; i += 20)
        {
            var hashBytes = new byte[20];
            Array.Copy(piecesBytes, i, hashBytes, 0, 20);
            hashes.Add(Convert.ToHexString(hashBytes).ToLower());
        }
        
        return hashes;
    }

    private int GetTotalPieces()
    {
        return (int)Math.Ceiling((double)_torrentInfo.TotalSize / _torrentInfo.PieceLength);
    }

    public List<PieceRequest> GetNextRequests(int maxRequests = 5)
    {
        lock (_lockObject)
        {
            var requests = new List<PieceRequest>();
            var random = new Random();
            
            // Get pieces that are not downloaded and not pending
            var availablePieces = _pieces
                .Where(p => !p.IsDownloaded && !_pendingRequests.ContainsKey(p.Index))
                .OrderBy(p => random.Next()) // Random selection for better distribution
                .Take(maxRequests)
                .ToList();

            foreach (var piece in availablePieces)
            {
                var pieceRequests = CreatePieceRequests(piece);
                _pendingRequests[piece.Index] = pieceRequests;
                requests.AddRange(pieceRequests);
            }

            return requests;
        }
    }

    private List<PieceRequest> CreatePieceRequests(Piece piece)
    {
        var requests = new List<PieceRequest>();
        const int blockSize = 16384; // 16KB blocks
        var remainingLength = piece.Length;
        var offset = 0;

        while (remainingLength > 0)
        {
            var blockLength = Math.Min(blockSize, remainingLength);
            requests.Add(new PieceRequest
            {
                PieceIndex = piece.Index,
                Offset = offset,
                Length = blockLength
            });

            offset += blockLength;
            remainingLength -= blockLength;
        }

        return requests;
    }

    public async Task<bool> StorePieceDataAsync(int pieceIndex, int offset, byte[] data)
    {
        lock (_lockObject)
        {
            var piece = _pieces.FirstOrDefault(p => p.Index == pieceIndex);
            if (piece == null) return false;

            // Initialize piece data if needed
            if (piece.Data == null)
                piece.Data = new byte[piece.Length];

            // Copy data to piece
            Array.Copy(data, 0, piece.Data, offset, data.Length);

            // Check if piece is complete
            if (IsPieceComplete(piece))
            {
                piece.IsDownloaded = true;
                _downloadedPieces[pieceIndex] = true;
                
                // Remove from pending requests
                _pendingRequests.Remove(pieceIndex);
                
                // Verify piece
                _ = Task.Run(async () => await VerifyPieceAsync(piece));
                
                PieceDownloaded?.Invoke(this, pieceIndex);
                UpdateProgress();
            }

            return true;
        }
    }

    private bool IsPieceComplete(Piece piece)
    {
        if (piece.Data == null) return false;
        
        // Check if all bytes are non-zero (simple check)
        return piece.Data.All(b => b != 0);
    }

    private async Task<bool> VerifyPieceAsync(Piece piece)
    {
        if (piece.Data == null) return false;

        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(piece.Data);
        var computedHash = Convert.ToHexString(hashBytes).ToLower();

        var isValid = computedHash == piece.Hash;

        lock (_lockObject)
        {
            piece.IsVerified = isValid;
            if (isValid)
            {
                PieceVerified?.Invoke(this, piece.Index);
            }
            else
            {
                // Reset piece for re-download
                piece.IsDownloaded = false;
                piece.Data = null;
                _downloadedPieces[piece.Index] = false;
            }
        }

        return isValid;
    }

    private void UpdateProgress()
    {
        var downloadedPieces = _downloadedPieces.Cast<bool>().Count(b => b);
        var totalPieces = _downloadedPieces.Length;
        var progress = (double)downloadedPieces / totalPieces;
        
        ProgressChanged?.Invoke(this, progress);
    }

    public async Task SaveCompletedPiecesAsync()
    {
        lock (_lockObject)
        {
            var verifiedPieces = _pieces.Where(p => p.IsVerified).ToList();
            
            foreach (var piece in verifiedPieces)
            {
                if (piece.Data != null)
                {
                    _ = Task.Run(async () => await SavePieceToFileAsync(piece));
                }
            }
        }
    }

    private async Task SavePieceToFileAsync(Piece piece)
    {
        try
        {
            // Determine which file(s) this piece belongs to
            var affectedFiles = GetAffectedFiles(piece);
            
            foreach (var file in affectedFiles)
            {
                var filePath = Path.Combine(_downloadPath, file.Path);
                var directory = Path.GetDirectoryName(filePath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                using var fileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write);
                
                // Calculate file offset within the piece
                var fileOffsetInPiece = Math.Max(0, file.Offset - piece.Offset);
                var bytesToWrite = Math.Min(piece.Length - fileOffsetInPiece, file.Length);
                
                if (bytesToWrite > 0 && piece.Data != null)
                {
                    fileStream.Seek(file.Offset, 0);
                    await fileStream.WriteAsync(piece.Data, (int)fileOffsetInPiece, (int)bytesToWrite);
                }
            }
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"Error saving piece {piece.Index}: {ex.Message}");
        }
    }

    private List<TorrentFile> GetAffectedFiles(Piece piece)
    {
        var affectedFiles = new List<TorrentFile>();
        var pieceStart = piece.Offset;
        var pieceEnd = piece.Offset + piece.Length;

        foreach (var file in _torrentInfo.Files)
        {
            var fileStart = file.Offset;
            var fileEnd = file.Offset + file.Length;

            // Check if piece overlaps with file
            if (pieceStart < fileEnd && pieceEnd > fileStart)
            {
                affectedFiles.Add(file);
            }
        }

        return affectedFiles;
    }

    public double GetProgress()
    {
        lock (_lockObject)
        {
            var downloadedPieces = _downloadedPieces.Cast<bool>().Count(b => b);
            var totalPieces = _downloadedPieces.Length;
            return (double)downloadedPieces / totalPieces;
        }
    }

    public int GetDownloadedPiecesCount()
    {
        lock (_lockObject)
        {
            return _downloadedPieces.Cast<bool>().Count(b => b);
        }
    }

    public int GetTotalPiecesCount()
    {
        return _downloadedPieces.Length;
    }

    public bool IsComplete()
    {
        lock (_lockObject)
        {
            return _downloadedPieces.Cast<bool>().All(b => b);
        }
    }

    public void CancelRequest(int pieceIndex, int offset, int length)
    {
        lock (_lockObject)
        {
            if (_pendingRequests.TryGetValue(pieceIndex, out var requests))
            {
                var requestToRemove = requests.FirstOrDefault(r => 
                    r.PieceIndex == pieceIndex && r.Offset == offset && r.Length == length);
                
                if (requestToRemove != null)
                {
                    requests.Remove(requestToRemove);
                    
                    if (requests.Count == 0)
                        _pendingRequests.Remove(pieceIndex);
                }
            }
        }
    }
}

public class PieceRequest
{
    public int PieceIndex { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
} 