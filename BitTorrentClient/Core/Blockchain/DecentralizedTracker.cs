using System.Security.Cryptography;
using System.Text;

namespace BitTorrentClient.Core.Blockchain;

public class DecentralizedTracker
{
    private readonly Dictionary<string, List<PeerInfo>> _peerCache = new();
    private readonly List<PeerNode> _knownNodes = new();
    private readonly Random _random = new();
    private readonly object _lockObject = new();

    public event EventHandler<List<PeerInfo>>? PeersDiscovered;

    public async Task<List<PeerInfo>> DiscoverPeersAsync(string infoHash, int maxPeers = 50)
    {
        var discoveredPeers = new List<PeerInfo>();
        
        // Try multiple discovery methods
        var methods = new List<Func<string, Task<List<PeerInfo>>>>
        {
            DiscoverViaDHT,
            DiscoverViaPeerExchange,
            DiscoverViaLocalCache,
            DiscoverViaGossip
        };

        foreach (var method in methods.OrderBy(_ => _random.Next()))
        {
            try
            {
                var peers = await method(infoHash);
                discoveredPeers.AddRange(peers);
                
                if (discoveredPeers.Count >= maxPeers)
                    break;
            }
            catch (Exception ex)
            {
                // Log error but continue with other methods
                Console.WriteLine($"Discovery method failed: {ex.Message}");
            }
        }

        // Remove duplicates and limit results
        return discoveredPeers
            .GroupBy(p => $"{p.Ip}:{p.Port}")
            .Select(g => g.First())
            .Take(maxPeers)
            .ToList();
    }

    private async Task<List<PeerInfo>> DiscoverViaDHT(string infoHash)
    {
        // Simulate DHT (Distributed Hash Table) discovery
        var peers = new List<PeerInfo>();
        
        // Generate some fake peers based on the info hash
        var hashBytes = Convert.FromHexString(infoHash);
        var seed = BitConverter.ToInt32(hashBytes, 0);
        var rng = new Random(seed);
        
        for (int i = 0; i < 10; i++)
        {
            peers.Add(new PeerInfo
            {
                Ip = $"192.168.{rng.Next(1, 255)}.{rng.Next(1, 255)}",
                Port = rng.Next(6881, 6890),
                PeerId = GeneratePeerId(rng)
            });
        }
        
        await Task.Delay(100); // Simulate network delay
        return peers;
    }

    private async Task<List<PeerInfo>> DiscoverViaPeerExchange(string infoHash)
    {
        // Simulate peer exchange protocol
        var peers = new List<PeerInfo>();
        
        // Get peers from known connections
        lock (_lockObject)
        {
            if (_peerCache.TryGetValue(infoHash, out var cachedPeers))
            {
                peers.AddRange(cachedPeers);
            }
        }
        
        await Task.Delay(50);
        return peers;
    }

    private async Task<List<PeerInfo>> DiscoverViaLocalCache(string infoHash)
    {
        // Check local peer cache
        lock (_lockObject)
        {
            if (_peerCache.TryGetValue(infoHash, out var peers))
            {
                return peers.Where(p => p.LastSeen > DateTime.Now.AddMinutes(-30)).ToList();
            }
        }
        
        await Task.Delay(10);
        return new List<PeerInfo>();
    }

    private async Task<List<PeerInfo>> DiscoverViaGossip(string infoHash)
    {
        // Simulate gossip protocol for peer discovery
        var peers = new List<PeerInfo>();
        
        // Ask known nodes for peers
        foreach (var node in _knownNodes.Take(5))
        {
            try
            {
                var nodePeers = await QueryNodeForPeers(node, infoHash);
                peers.AddRange(nodePeers);
            }
            catch
            {
                // Node might be offline, continue
            }
        }
        
        return peers;
    }

    private async Task<List<PeerInfo>> QueryNodeForPeers(PeerNode node, string infoHash)
    {
        // Simulate querying a node for peers
        await Task.Delay(200);
        
        var peers = new List<PeerInfo>();
        var rng = new Random(node.GetHashCode());
        
        for (int i = 0; i < rng.Next(1, 5); i++)
        {
            peers.Add(new PeerInfo
            {
                Ip = $"10.0.{rng.Next(1, 255)}.{rng.Next(1, 255)}",
                Port = rng.Next(6881, 6890),
                PeerId = GeneratePeerId(rng)
            });
        }
        
        return peers;
    }

    public void AddPeerToCache(string infoHash, PeerInfo peer)
    {
        lock (_lockObject)
        {
            if (!_peerCache.ContainsKey(infoHash))
                _peerCache[infoHash] = new List<PeerInfo>();
            
            // Update existing peer or add new one
            var existingPeer = _peerCache[infoHash].FirstOrDefault(p => 
                p.Ip == peer.Ip && p.Port == peer.Port);
            
            if (existingPeer != null)
            {
                existingPeer.LastSeen = DateTime.Now;
                existingPeer.DownloadSpeed = peer.DownloadSpeed;
                existingPeer.UploadSpeed = peer.UploadSpeed;
            }
            else
            {
                peer.LastSeen = DateTime.Now;
                _peerCache[infoHash].Add(peer);
            }
        }
    }

    public void AddKnownNode(string ip, int port)
    {
        lock (_lockObject)
        {
            var node = new PeerNode { Ip = ip, Port = port };
            if (!_knownNodes.Any(n => n.Ip == ip && n.Port == port))
            {
                _knownNodes.Add(node);
            }
        }
    }

    public void BroadcastPeerInfo(string infoHash, PeerInfo peer)
    {
        // Broadcast peer information to other nodes
        lock (_lockObject)
        {
            foreach (var node in _knownNodes)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await BroadcastToNode(node, infoHash, peer);
                    }
                    catch
                    {
                        // Node might be offline
                    }
                });
            }
        }
    }

    private async Task BroadcastToNode(PeerNode node, string infoHash, PeerInfo peer)
    {
        // Simulate broadcasting peer info to a node
        await Task.Delay(100);
        
        // In a real implementation, this would send a UDP packet
        // with the peer information to the node
    }

    private string GeneratePeerId(Random rng)
    {
        var bytes = new byte[20];
        rng.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }

    public void CleanupOldPeers()
    {
        lock (_lockObject)
        {
            var cutoff = DateTime.Now.AddHours(-1);
            
            foreach (var kvp in _peerCache)
            {
                kvp.Value.RemoveAll(p => p.LastSeen < cutoff);
            }
            
            // Remove empty entries
            var emptyKeys = _peerCache.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
            foreach (var key in emptyKeys)
            {
                _peerCache.Remove(key);
            }
        }
    }
}

public class PeerNode
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
}

public class PeerInfo
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; }
    public string PeerId { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public double DownloadSpeed { get; set; }
    public double UploadSpeed { get; set; }
    public bool IsPreferred { get; set; }
} 