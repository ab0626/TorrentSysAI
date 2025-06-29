using System.Collections.Concurrent;

namespace BitTorrentClient.Core.AI;

public class PeerPerformance
{
    public string PeerId { get; set; } = string.Empty;
    public double AverageSpeed { get; set; }
    public double Reliability { get; set; } // 0-1, based on connection stability
    public int SuccessfulPieces { get; set; }
    public int FailedPieces { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsPreferred { get; set; }
    public double Score => CalculateScore();

    private double CalculateScore()
    {
        var speedScore = Math.Min(AverageSpeed / 1024 / 1024, 10); // Cap at 10 MB/s
        var reliabilityScore = Reliability * 10;
        var successRate = SuccessfulPieces / (double)Math.Max(SuccessfulPieces + FailedPieces, 1);
        var responseScore = Math.Max(0, 10 - AverageResponseTime.TotalSeconds);
        
        return speedScore * 0.4 + reliabilityScore * 0.3 + successRate * 0.2 + responseScore * 0.1;
    }
}

public class IntelligentPeerSelector
{
    private readonly ConcurrentDictionary<string, PeerPerformance> _peerHistory = new();
    private readonly List<string> _preferredPeers = new();
    private readonly Random _random = new();
    private readonly object _lockObject = new();

    public event EventHandler<string>? PeerBlacklisted;
    public event EventHandler<string>? PeerPromoted;

    public void RecordPeerPerformance(string peerId, double speed, bool pieceSuccess, TimeSpan responseTime)
    {
        var performance = _peerHistory.GetOrAdd(peerId, _ => new PeerPerformance { PeerId = peerId });
        
        lock (_lockObject)
        {
            // Update average speed using exponential moving average
            performance.AverageSpeed = performance.AverageSpeed * 0.9 + speed * 0.1;
            
            // Update reliability based on piece success
            var successRate = pieceSuccess ? 1.0 : 0.0;
            performance.Reliability = performance.Reliability * 0.95 + successRate * 0.05;
            
            // Update piece counts
            if (pieceSuccess)
                performance.SuccessfulPieces++;
            else
                performance.FailedPieces++;
            
            // Update response time
            performance.AverageResponseTime = TimeSpan.FromMilliseconds(
                performance.AverageResponseTime.TotalMilliseconds * 0.9 + responseTime.TotalMilliseconds * 0.1);
            
            performance.LastSeen = DateTime.Now;
            
            // Check for promotion/demotion
            CheckPeerStatus(performance);
        }
    }

    private void CheckPeerStatus(PeerPerformance performance)
    {
        var wasPreferred = performance.IsPreferred;
        
        // Promote peer if score is high and reliability is good
        if (performance.Score > 7.0 && performance.Reliability > 0.8 && !performance.IsPreferred)
        {
            performance.IsPreferred = true;
            _preferredPeers.Add(performance.PeerId);
            PeerPromoted?.Invoke(this, performance.PeerId);
        }
        
        // Demote peer if performance is poor
        if (performance.Score < 2.0 && performance.IsPreferred)
        {
            performance.IsPreferred = false;
            _preferredPeers.Remove(performance.PeerId);
        }
        
        // Blacklist peer if consistently failing
        if (performance.FailedPieces > 10 && performance.Reliability < 0.3)
        {
            PeerBlacklisted?.Invoke(this, performance.PeerId);
        }
    }

    public List<string> GetOptimalPeerOrder(List<string> availablePeers, int maxPeers = 10)
    {
        var scoredPeers = new List<(string peerId, double score)>();
        
        foreach (var peerId in availablePeers)
        {
            var score = GetPeerScore(peerId);
            scoredPeers.Add((peerId, score));
        }
        
        // Sort by score (highest first) with some randomness for diversity
        return scoredPeers
            .OrderByDescending(p => p.score + _random.NextDouble() * 0.5)
            .Take(maxPeers)
            .Select(p => p.peerId)
            .ToList();
    }

    private double GetPeerScore(string peerId)
    {
        if (_peerHistory.TryGetValue(peerId, out var performance))
        {
            return performance.Score;
        }
        
        // New peers get a moderate score to give them a chance
        return 5.0;
    }

    public List<string> GetPreferredPeers()
    {
        return _preferredPeers.ToList();
    }

    public PeerPerformance? GetPeerStats(string peerId)
    {
        _peerHistory.TryGetValue(peerId, out var performance);
        return performance;
    }

    public Dictionary<string, double> GetPeerScores()
    {
        return _peerHistory.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Score);
    }

    public void ClearHistory()
    {
        _peerHistory.Clear();
        _preferredPeers.Clear();
    }
} 