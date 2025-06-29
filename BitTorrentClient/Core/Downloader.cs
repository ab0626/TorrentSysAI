using System.Collections.Concurrent;
using BitTorrentClient.Core.Privacy;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace BitTorrentClient.Core;

public enum TorrentStatus
{
    Stopped,
    Starting,
    Downloading,
    Seeding,
    Paused,
    Error,
    Completed
}

public class TorrentStats
{
    public long Downloaded { get; set; }
    public long Uploaded { get; set; }
    public long Left { get; set; }
    public double Progress { get; set; }
    public int ConnectedPeers { get; set; }
    public int TotalPeers { get; set; }
    public double DownloadSpeed { get; set; }
    public double UploadSpeed { get; set; }
    public TimeSpan EstimatedTime { get; set; }
}

public class TorrentDownloader : IDisposable
{
    private readonly TorrentMetadata _metadata;
    private readonly string _downloadPath;
    private readonly StealthMode _stealthMode;
    private readonly string _peerId;
    private readonly int _listenPort;
    private readonly TrackerClient _trackerClient;
    private readonly PieceManager _pieceManager;
    private readonly ConcurrentDictionary<string, PeerConnection> _peers = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Timer _trackerTimer;
    private readonly Timer _statsTimer;
    private readonly object _lockObject = new();

    private TorrentStatus _status = TorrentStatus.Stopped;
    private long _downloaded = 0;
    private long _uploaded = 0;
    private DateTime _lastStatsUpdate = DateTime.Now;
    private double _downloadSpeed = 0;
    private double _uploadSpeed = 0;

    private static readonly string DebugLogPath = "debug.log";
    private static void LogDebug(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(logLine);
        try { System.IO.File.AppendAllText(DebugLogPath, logLine + "\n"); } catch { }
    }

    public event EventHandler<TorrentStatus>? StatusChanged;
    public event EventHandler<TorrentStats>? StatsUpdated;
    public event EventHandler<string>? ErrorOccurred;

    public TorrentDownloader(TorrentMetadata metadata, string downloadPath, int listenPort = 6882, StealthMode? stealthMode = null)
    {
        _metadata = metadata;
        _downloadPath = downloadPath;
        _stealthMode = stealthMode ?? new StealthMode();
        _stealthMode.IsEnabled = false; // Disable stealth mode by default
        _listenPort = _stealthMode.IsEnabled ? int.Parse(_stealthMode.GetRandomPort()) : listenPort;
        _peerId = _stealthMode.IsEnabled ? _stealthMode.CurrentPeerId : GeneratePeerId();
        _trackerClient = new TrackerClient(_stealthMode);
        _pieceManager = new PieceManager(metadata.Info, downloadPath);
        _cancellationTokenSource = new CancellationTokenSource();

        // Set up timers
        _trackerTimer = new Timer(UpdateTrackerAsync, null, Timeout.Infinite, Timeout.Infinite);
        _statsTimer = new Timer(UpdateStatsAsync, null, Timeout.Infinite, Timeout.Infinite);

        // Subscribe to piece manager events
        _pieceManager.PieceDownloaded += OnPieceDownloaded;
        _pieceManager.PieceVerified += OnPieceVerified;
        _pieceManager.ProgressChanged += OnProgressChanged;
    }

    public async Task StartAsync()
    {
        try
        {
            SetStatus(TorrentStatus.Starting);

            // Create download directory
            Directory.CreateDirectory(_downloadPath);

            LogDebug($"[DEBUG] Starting torrent: {_metadata.Info.Name}");

            // Start tracker updates
            await UpdateTrackerAsync();
            _trackerTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            // Start stats updates
            _statsTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            SetStatus(TorrentStatus.Downloading);
        }
        catch (Exception ex)
        {
            SetStatus(TorrentStatus.Error);
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    public async Task StopAsync()
    {
        try
        {
            SetStatus(TorrentStatus.Stopped);

            // Stop timers
            _trackerTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _statsTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // Announce stopped to tracker
            await AnnounceToTrackerAsync("stopped");

            // Disconnect all peers
            foreach (var peer in _peers.Values)
            {
                peer.Dispose();
            }
            _peers.Clear();

            // Save completed pieces
            await _pieceManager.SaveCompletedPiecesAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex.Message);
        }
    }

    public async Task PauseAsync()
    {
        SetStatus(TorrentStatus.Paused);
        _trackerTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _statsTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public async Task ResumeAsync()
    {
        SetStatus(TorrentStatus.Downloading);
        _trackerTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _statsTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private async void UpdateTrackerAsync(object? state)
    {
        try
        {
            await UpdateTrackerAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Tracker update failed: {ex.Message}");
        }
    }

    private async Task UpdateTrackerAsync()
    {
        var request = new TrackerRequest
        {
            InfoHash = _metadata.Info.InfoHash,
            PeerId = _peerId,
            Port = _listenPort,
            Uploaded = _uploaded,
            Downloaded = _downloaded,
            Left = _metadata.Info.TotalSize - _downloaded,
            Event = _status == TorrentStatus.Starting ? "started" : "update",
            NumWant = 50
        };

        LogDebug($"[DEBUG] Announcing to tracker: {_metadata.Announce}");
        var response = await _trackerClient.AnnounceAsync(_metadata.Announce, request);
        LogDebug($"[DEBUG] Tracker response: FailureReason={response.FailureReason}, Peers={response.Peers.Count}");

        if (!string.IsNullOrEmpty(response.FailureReason))
        {
            ErrorOccurred?.Invoke(this, $"Tracker error: {response.FailureReason}");
            return;
        }

        // Connect to new peers
        foreach (var peerInfo in response.Peers)
        {
            if (!_peers.ContainsKey($"{peerInfo.Ip}:{peerInfo.Port}"))
            {
                LogDebug($"[DEBUG] Attempting to connect to peer: {peerInfo.Ip}:{peerInfo.Port}");
                _ = Task.Run(async () => await ConnectToPeerAsync(peerInfo));
            }
        }
    }

    private async Task ConnectToPeerAsync(PeerInfo peerInfo)
    {
        try
        {
            // Validate peer address
            if (string.IsNullOrEmpty(peerInfo.Ip) || peerInfo.Port <= 0 || peerInfo.Port > 65535)
            {
                LogDebug($"[DOWNLOADER] Skipping invalid peer: {peerInfo.Ip}:{peerInfo.Port}");
                return;
            }

            // Skip localhost and private IPs for now (we're not implementing local peer discovery)
            if (IsPrivateOrLocalAddress(peerInfo.Ip))
            {
                LogDebug($"[DOWNLOADER] Skipping private/local peer: {peerInfo.Ip}:{peerInfo.Port}");
                return;
            }

            LogDebug($"[DOWNLOADER] Attempting to connect to peer: {peerInfo.Ip}:{peerInfo.Port}");
            
            var peer = new PeerConnection(peerInfo.Ip, peerInfo.Port, _stealthMode.IsEnabled ? _stealthMode.CurrentPeerId : _peerId, _metadata.Info.InfoHash, _listenPort, _stealthMode);
            
            peer.MessageReceived += OnPeerMessageReceived;
            peer.ConnectionError += OnPeerConnectionError;
            peer.ConnectionClosed += OnPeerConnectionClosed;

            await peer.ConnectAsync();

            var peerKey = $"{peerInfo.Ip}:{peerInfo.Port}";
            _peers.TryAdd(peerKey, peer);

            LogDebug($"[DOWNLOADER] Successfully connected to peer: {peerInfo.Ip}:{peerInfo.Port}, total peers: {_peers.Count}");

            // Send interested message
            await peer.SendInterestedAsync();
            LogDebug($"[DOWNLOADER] Sent interested message to {peerInfo.Ip}:{peerInfo.Port}");

            // Start requesting pieces
            _ = Task.Run(async () => await RequestPiecesFromPeerAsync(peer));
        }
        catch (Exception ex)
        {
            LogDebug($"[DOWNLOADER] Failed to connect to peer {peerInfo.Ip}:{peerInfo.Port}: {ex.Message}");
            ErrorOccurred?.Invoke(this, $"Failed to connect to peer {peerInfo.Ip}:{peerInfo.Port}: {ex.Message}");
        }
    }

    private bool IsPrivateOrLocalAddress(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return true;
        
        // Check for localhost
        if (ip == "127.0.0.1" || ip == "localhost") return true;
        
        // Check for private IP ranges
        var parts = ip.Split('.');
        if (parts.Length != 4) return true;
        
        if (!int.TryParse(parts[0], out var first) || 
            !int.TryParse(parts[1], out var second) || 
            !int.TryParse(parts[2], out var third) || 
            !int.TryParse(parts[3], out var fourth))
            return true;
        
        // Private IP ranges
        if (first == 10) return true; // 10.0.0.0/8
        if (first == 172 && second >= 16 && second <= 31) return true; // 172.16.0.0/12
        if (first == 192 && second == 168) return true; // 192.168.0.0/16
        
        return false;
    }

    private async Task RequestPiecesFromPeerAsync(PeerConnection peer)
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested && 
                   peer.State == PeerState.Connected && 
                   !peer.IsChoked)
            {
                var requests = _pieceManager.GetNextRequests(5);
                
                foreach (var request in requests)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) break;
                    
                    await peer.SendRequestAsync(request.PieceIndex, request.Offset, request.Length);
                    await Task.Delay(100); // Small delay between requests
                }

                await Task.Delay(1000); // Wait before next batch
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Error requesting pieces: {ex.Message}");
        }
    }

    private void OnPeerMessageReceived(object? sender, PeerMessage message)
    {
        if (sender is not PeerConnection peer) return;

        LogDebug($"[DOWNLOADER] Received message from {peer.Ip}:{peer.Port}: {message.MessageType}");

        switch (message.MessageType)
        {
            case PeerMessageType.Piece:
                HandlePieceMessage(peer, message);
                break;
            case PeerMessageType.Unchoke:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} unchoked us, starting piece requests");
                // Start requesting pieces from this peer
                _ = Task.Run(async () => await RequestPiecesFromPeerAsync(peer));
                break;
            case PeerMessageType.Bitfield:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} sent bitfield");
                break;
            case PeerMessageType.Choke:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} choked us");
                break;
            case PeerMessageType.Interested:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} is interested");
                break;
            case PeerMessageType.NotInterested:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} is not interested");
                break;
            case PeerMessageType.Have:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} has piece");
                break;
            case PeerMessageType.Request:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} requested piece");
                break;
            case PeerMessageType.Cancel:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} cancelled request");
                break;
            case PeerMessageType.KeepAlive:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} keep-alive");
                break;
            default:
                LogDebug($"[DOWNLOADER] Peer {peer.Ip}:{peer.Port} unknown message: {message.MessageType}");
                break;
        }
    }

    private async void HandlePieceMessage(PeerConnection peer, PeerMessage message)
    {
        if (message.Payload == null || message.Payload.Length < 8) return;

        var pieceIndex = BitConverter.ToInt32(message.Payload, 0);
        var offset = BitConverter.ToInt32(message.Payload, 4);
        
        if (BitConverter.IsLittleEndian)
        {
            pieceIndex = IPAddress.NetworkToHostOrder(pieceIndex);
            offset = IPAddress.NetworkToHostOrder(offset);
        }

        var data = new byte[message.Payload.Length - 8];
        Array.Copy(message.Payload, 8, data, 0, data.Length);

        await _pieceManager.StorePieceDataAsync(pieceIndex, offset, data);
    }

    private void OnPeerConnectionError(object? sender, Exception ex)
    {
        if (sender is PeerConnection peer)
        {
            var peerKey = $"{peer.Ip}:{peer.Port}";
            LogDebug($"[DOWNLOADER] Peer connection error for {peerKey}: {ex.Message}");
            _peers.TryRemove(peerKey, out _);
            peer.Dispose();
            LogDebug($"[DOWNLOADER] Removed peer {peerKey}, total peers: {_peers.Count}");
        }
    }

    private void OnPeerConnectionClosed(object? sender, EventArgs e)
    {
        if (sender is PeerConnection peer)
        {
            var peerKey = $"{peer.Ip}:{peer.Port}";
            LogDebug($"[DOWNLOADER] Peer connection closed for {peerKey}");
            _peers.TryRemove(peerKey, out _);
            LogDebug($"[DOWNLOADER] Removed peer {peerKey}, total peers: {_peers.Count}");
        }
    }

    private void OnPieceDownloaded(object? sender, int pieceIndex)
    {
        _downloaded += _metadata.Info.PieceLength;
    }

    private void OnPieceVerified(object? sender, int pieceIndex)
    {
        // Piece verified successfully
    }

    private void OnProgressChanged(object? sender, double progress)
    {
        if (progress >= 1.0)
        {
            SetStatus(TorrentStatus.Completed);
            _ = Task.Run(async () => await AnnounceToTrackerAsync("completed"));
        }
    }

    private async void UpdateStatsAsync(object? state)
    {
        try
        {
            await UpdateStatsAsync();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Stats update failed: {ex.Message}");
        }
    }

    private async Task UpdateStatsAsync()
    {
        var now = DateTime.Now;
        var timeDiff = (now - _lastStatsUpdate).TotalSeconds;

        if (timeDiff > 0)
        {
            _downloadSpeed = _pieceManager.GetProgress() / timeDiff;
            _uploadSpeed = 0; // TODO: Implement upload tracking
        }

        _lastStatsUpdate = now;

        // Count only connected peers
        var connectedPeers = _peers.Values.Count(p => p.State == PeerState.Connected);

        var stats = new TorrentStats
        {
            Downloaded = _downloaded,
            Uploaded = _uploaded,
            Left = _metadata.Info.TotalSize - _downloaded,
            Progress = _pieceManager.GetProgress(),
            ConnectedPeers = connectedPeers,
            TotalPeers = _peers.Count,
            DownloadSpeed = _downloadSpeed,
            UploadSpeed = _uploadSpeed,
            EstimatedTime = CalculateEstimatedTime()
        };

        LogDebug($"[DOWNLOADER] Stats update - Connected: {connectedPeers}/{_peers.Count}, Progress: {stats.Progress:P2}, Downloaded: {stats.Downloaded}");

        StatsUpdated?.Invoke(this, stats);
    }

    private TimeSpan CalculateEstimatedTime()
    {
        if (_downloadSpeed <= 0) return TimeSpan.Zero;

        var remainingBytes = _metadata.Info.TotalSize - _downloaded;
        var seconds = remainingBytes / _downloadSpeed;
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task AnnounceToTrackerAsync(string eventType)
    {
        try
        {
            var request = new TrackerRequest
            {
                InfoHash = _metadata.Info.InfoHash,
                PeerId = _peerId,
                Port = _listenPort,
                Uploaded = _uploaded,
                Downloaded = _downloaded,
                Left = _metadata.Info.TotalSize - _downloaded,
                Event = eventType
            };

            await _trackerClient.AnnounceAsync(_metadata.Announce, request);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Tracker announcement failed: {ex.Message}");
        }
    }

    private void SetStatus(TorrentStatus status)
    {
        lock (_lockObject)
        {
            _status = status;
        }
        StatusChanged?.Invoke(this, status);
    }

    private string GeneratePeerId()
    {
        var random = new Random();
        var peerId = new byte[20];
        random.NextBytes(peerId);
        
        // Set client identifier (e.g., "-BT0001-")
        var clientId = Encoding.ASCII.GetBytes("-BT0001-");
        Array.Copy(clientId, 0, peerId, 0, Math.Min(clientId.Length, peerId.Length));
        
        return Encoding.ASCII.GetString(peerId);
    }

    public TorrentStatus GetStatus()
    {
        lock (_lockObject)
        {
            return _status;
        }
    }

    public TorrentStats GetStats()
    {
        // Count only connected peers
        var connectedPeers = _peers.Values.Count(p => p.State == PeerState.Connected);

        return new TorrentStats
        {
            Downloaded = _downloaded,
            Uploaded = _uploaded,
            Left = _metadata.Info.TotalSize - _downloaded,
            Progress = _pieceManager.GetProgress(),
            ConnectedPeers = connectedPeers,
            TotalPeers = _peers.Count,
            DownloadSpeed = _downloadSpeed,
            UploadSpeed = _uploadSpeed,
            EstimatedTime = CalculateEstimatedTime()
        };
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _trackerTimer?.Dispose();
        _statsTimer?.Dispose();
        _trackerClient?.Dispose();
        
        foreach (var peer in _peers.Values)
        {
            peer.Dispose();
        }
        _peers.Clear();
        
        _cancellationTokenSource.Dispose();
    }
} 