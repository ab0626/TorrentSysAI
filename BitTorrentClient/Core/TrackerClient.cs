using System.Text;
using System.Web;
using System.Net.Http;
using BitTorrentClient.Core.Privacy;

namespace BitTorrentClient.Core;

public class TrackerResponse
{
    public string FailureReason { get; set; } = string.Empty;
    public string WarningMessage { get; set; } = string.Empty;
    public long Interval { get; set; }
    public long MinInterval { get; set; }
    public string TrackerId { get; set; } = string.Empty;
    public long Complete { get; set; }
    public long Incomplete { get; set; }
    public List<PeerInfo> Peers { get; set; } = new();
}

public class PeerInfo
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public string PeerId { get; set; } = string.Empty;
}

public class TrackerClient
{
    private readonly HttpClient _httpClient;
    private readonly Random _random;
    private readonly StealthMode? _stealthMode;

    public TrackerClient(StealthMode? stealthMode = null)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _random = new Random();
        _stealthMode = stealthMode;
    }

    public async Task<TrackerResponse> AnnounceAsync(string trackerUrl, TrackerRequest request)
    {
        try
        {
            // Randomize IP if StealthMode is enabled
            if (_stealthMode != null && _stealthMode.IsEnabled)
            {
                request.Ip = _stealthMode.GetRandomIP();
            }

            var queryString = BuildQueryString(request);

            // Encrypt query string if StealthMode is enabled
            if (_stealthMode != null && _stealthMode.IsEnabled)
            {
                var encrypted = _stealthMode.EncryptTrackerRequest(Encoding.UTF8.GetBytes(queryString));
                // Encode as base64 to make it URL-safe
                queryString = "enc=" + Convert.ToBase64String(encrypted);
            }

            var fullUrl = $"{trackerUrl}?{queryString}";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            if (_stealthMode != null && _stealthMode.IsEnabled)
            {
                foreach (var header in _stealthMode.GetStealthHeaders())
                {
                    if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                        httpRequest.Headers.UserAgent.ParseAdd(header.Value);
                    else
                        httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseData = await response.Content.ReadAsByteArrayAsync();
            System.IO.File.WriteAllBytes("tracker_response_raw.bin", responseData); // Save raw response for inspection
            Console.WriteLine($"[DEBUG] Raw tracker response bytes: {responseData.Length}");

            var decoded = BencodeParser.Decode(responseData);

            if (decoded is not Dictionary<string, object> dict)
                throw new ArgumentException("Invalid tracker response format");

            return ParseTrackerResponse(dict);
        }
        catch (Exception ex)
        {
            return new TrackerResponse
            {
                FailureReason = $"Tracker request failed: {ex.Message}"
            };
        }
    }

    private string BuildQueryString(TrackerRequest request)
    {
        var parameters = new Dictionary<string, string>
        {
            ["info_hash"] = HttpUtility.UrlEncode(Convert.FromHexString(request.InfoHash)),
            ["peer_id"] = HttpUtility.UrlEncode(Encoding.ASCII.GetBytes(request.PeerId)),
            ["port"] = request.Port.ToString(),
            ["uploaded"] = request.Uploaded.ToString(),
            ["downloaded"] = request.Downloaded.ToString(),
            ["left"] = request.Left.ToString(),
            ["compact"] = "1",
            ["numwant"] = request.NumWant.ToString()
        };

        if (!string.IsNullOrEmpty(request.Event))
            parameters["event"] = request.Event;

        if (request.Ip != null)
            parameters["ip"] = request.Ip;

        if (request.Key != null)
            parameters["key"] = request.Key;

        if (request.TrackerId != null)
            parameters["trackerid"] = request.TrackerId;

        return string.Join("&", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }

    private TrackerResponse ParseTrackerResponse(Dictionary<string, object> dict)
    {
        var response = new TrackerResponse();

        // Parse failure reason
        if (dict.TryGetValue("failure reason", out var failureReason) && failureReason is string failureReasonStr)
            response.FailureReason = failureReasonStr;

        // Parse warning message
        if (dict.TryGetValue("warning message", out var warningMessage) && warningMessage is string warningMessageStr)
            response.WarningMessage = warningMessageStr;

        // Parse interval
        if (dict.TryGetValue("interval", out var interval) && interval is long intervalLong)
            response.Interval = intervalLong;

        // Parse min interval
        if (dict.TryGetValue("min interval", out var minInterval) && minInterval is long minIntervalLong)
            response.MinInterval = minIntervalLong;

        // Parse tracker id
        if (dict.TryGetValue("tracker id", out var trackerId) && trackerId is string trackerIdStr)
            response.TrackerId = trackerIdStr;

        // Parse complete
        if (dict.TryGetValue("complete", out var complete) && complete is long completeLong)
            response.Complete = completeLong;

        // Parse incomplete
        if (dict.TryGetValue("incomplete", out var incomplete) && incomplete is long incompleteLong)
            response.Incomplete = incompleteLong;

        // Parse peers
        if (dict.TryGetValue("peers", out var peers))
        {
            Console.WriteLine($"[DEBUG] 'peers' field type: {peers.GetType()}, value: {peers}");
            if (peers is byte[] peersBytes)
            {
                Console.WriteLine($"[DEBUG] 'peers' is byte[] of length {peersBytes.Length}");
                // Compact format
                response.Peers = ParseCompactPeers(peersBytes);
            }
            else if (peers is List<object> peersList)
            {
                Console.WriteLine($"[DEBUG] 'peers' is List<object> of count {peersList.Count}");
                // Dictionary format
                response.Peers = ParseDictionaryPeers(peersList);
            }
            else
            {
                Console.WriteLine($"[DEBUG] 'peers' is of unexpected type: {peers.GetType()}");
            }
        }

        return response;
    }

    private List<PeerInfo> ParseCompactPeers(byte[] peersBytes)
    {
        var peers = new List<PeerInfo>();
        for (int i = 0; i + 5 < peersBytes.Length; i += 6)
        {
            var ipBytes = new byte[4];
            Array.Copy(peersBytes, i, ipBytes, 0, 4);
            var portBytes = new byte[2];
            Array.Copy(peersBytes, i + 4, portBytes, 0, 2);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(portBytes);
            var port = BitConverter.ToUInt16(portBytes, 0);
            var ip = string.Join(".", ipBytes);
            Console.WriteLine($"[DEBUG] Parsed peer: {ip}:{port}");
            peers.Add(new PeerInfo
            {
                Ip = ip,
                Port = port,
                PeerId = string.Empty // Not available in compact format
            });
        }
        Console.WriteLine($"[DEBUG] Total peers parsed: {peers.Count}");
        return peers;
    }

    private List<PeerInfo> ParseDictionaryPeers(List<object> peersList)
    {
        var peers = new List<PeerInfo>();

        foreach (var peerObj in peersList)
        {
            if (peerObj is Dictionary<string, object> peerDict)
            {
                var peer = new PeerInfo();

                if (peerDict.TryGetValue("ip", out var ip) && ip is string ipStr)
                    peer.Ip = ipStr;

                if (peerDict.TryGetValue("port", out var port) && port is long portLong)
                    peer.Port = (int)portLong;

                if (peerDict.TryGetValue("peer id", out var peerId) && peerId is string peerIdStr)
                    peer.PeerId = peerIdStr;

                peers.Add(peer);
            }
        }

        return peers;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    // Optional: Method to rotate identity
    public void RotateIdentity()
    {
        _stealthMode?.RotateIdentity();
    }
}

public class TrackerRequest
{
    public string InfoHash { get; set; } = string.Empty;
    public string PeerId { get; set; } = string.Empty;
    public int Port { get; set; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public long Left { get; set; }
    public string Event { get; set; } = string.Empty; // started, stopped, completed
    public string? Ip { get; set; }
    public string? Key { get; set; }
    public string? TrackerId { get; set; }
    public int NumWant { get; set; } = 50;
} 