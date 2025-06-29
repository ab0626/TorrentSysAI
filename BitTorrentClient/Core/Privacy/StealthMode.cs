using System.Security.Cryptography;
using System.Text;

namespace BitTorrentClient.Core.Privacy;

public class StealthMode
{
    private readonly Dictionary<string, string> _peerIdMasks = new();
    private readonly Random _random = new();
    private bool _isEnabled = false;
    private string _currentPeerId = string.Empty;

    public event EventHandler<string>? PeerIdChanged;
    public event EventHandler<bool>? StealthModeToggled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                if (_isEnabled)
                {
                    EnableStealthMode();
                }
                else
                {
                    DisableStealthMode();
                }
                StealthModeToggled?.Invoke(this, _isEnabled);
            }
        }
    }

    public string CurrentPeerId => _currentPeerId;

    private void EnableStealthMode()
    {
        // Generate a new random peer ID that doesn't identify our client
        _currentPeerId = GenerateStealthPeerId();
        PeerIdChanged?.Invoke(this, _currentPeerId);
    }

    private void DisableStealthMode()
    {
        // Restore original peer ID
        _currentPeerId = GenerateStandardPeerId();
        PeerIdChanged?.Invoke(this, _currentPeerId);
    }

    private string GenerateStealthPeerId()
    {
        // Generate a peer ID that looks like a different client
        var clientPrefixes = new[]
        {
            "-AZ2060-", // Azureus/Vuze
            "-UT3530-", // uTorrent
            "-TR0070-", // Transmission
            "-qB3340-", // qBittorrent
            "-DE1870-", // Deluge
            "-LT1940-", // libtorrent
            "-AR1870-", // Ares
            "-MT1870-"  // Mainline
        };

        var prefix = clientPrefixes[_random.Next(clientPrefixes.Length)];
        var randomBytes = new byte[12];
        _random.NextBytes(randomBytes);
        
        var randomPart = Convert.ToHexString(randomBytes).ToLower();
        return prefix + randomPart;
    }

    private string GenerateStandardPeerId()
    {
        // Standard BitTorrent client identifier
        return "-BT0001-" + Convert.ToHexString(new byte[12]).ToLower();
    }

    public string GetMaskedPeerId(string originalPeerId)
    {
        if (!_isEnabled) return originalPeerId;

        if (!_peerIdMasks.TryGetValue(originalPeerId, out var maskedId))
        {
            maskedId = GenerateStealthPeerId();
            _peerIdMasks[originalPeerId] = maskedId;
        }

        return maskedId;
    }

    public string GetRandomUserAgent()
    {
        if (!_isEnabled) return "BitTorrent/7.10.3";

        var userAgents = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36",
            "uTorrent/3.5.5",
            "Transmission/3.00",
            "qBittorrent/4.4.2",
            "Deluge/2.0.3",
            "Vuze/5.7.6.0"
        };

        return userAgents[_random.Next(userAgents.Length)];
    }

    public byte[] EncryptTrackerRequest(byte[] data)
    {
        if (!_isEnabled) return data;

        // Simple XOR encryption with a random key
        var key = new byte[data.Length];
        _random.NextBytes(key);
        
        var encrypted = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            encrypted[i] = (byte)(data[i] ^ key[i]);
        }

        // Prepend the key for decryption
        var result = new byte[key.Length + encrypted.Length];
        key.CopyTo(result, 0);
        encrypted.CopyTo(result, key.Length);
        
        return result;
    }

    public byte[] DecryptTrackerRequest(byte[] encryptedData)
    {
        if (!_isEnabled) return encryptedData;

        // Extract key and decrypt
        var keyLength = encryptedData.Length / 2;
        var key = new byte[keyLength];
        var data = new byte[keyLength];
        
        Array.Copy(encryptedData, 0, key, 0, keyLength);
        Array.Copy(encryptedData, keyLength, data, 0, keyLength);

        var decrypted = new byte[keyLength];
        for (int i = 0; i < keyLength; i++)
        {
            decrypted[i] = (byte)(data[i] ^ key[i]);
        }

        return decrypted;
    }

    public string GetRandomPort()
    {
        if (!_isEnabled) return "6882";

        // Use a random port in the dynamic range
        return _random.Next(49152, 65535).ToString();
    }

    public string GetRandomIP()
    {
        if (!_isEnabled) return "127.0.0.1";

        // Generate a random private IP address
        var segments = new[]
        {
            _random.Next(10, 11),      // 10.x.x.x
            _random.Next(1, 255),
            _random.Next(1, 255),
            _random.Next(1, 255)
        };

        return string.Join(".", segments);
    }

    public Dictionary<string, string> GetStealthHeaders()
    {
        var headers = new Dictionary<string, string>();

        if (_isEnabled)
        {
            headers["User-Agent"] = GetRandomUserAgent();
            headers["X-Forwarded-For"] = GetRandomIP();
            headers["X-Real-IP"] = GetRandomIP();
            headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            headers["Accept-Language"] = "en-US,en;q=0.5";
            headers["Accept-Encoding"] = "gzip, deflate";
            headers["Connection"] = "keep-alive";
        }

        return headers;
    }

    public void RotateIdentity()
    {
        if (_isEnabled)
        {
            _currentPeerId = GenerateStealthPeerId();
            PeerIdChanged?.Invoke(this, _currentPeerId);
        }
    }

    public void ClearMasks()
    {
        _peerIdMasks.Clear();
    }

    public bool IsTrafficDetectable()
    {
        // Check if current traffic patterns could be detected as BitTorrent
        return !_isEnabled;
    }

    public string GetStealthStatus()
    {
        if (!_isEnabled)
            return "Stealth Mode: Disabled";

        return $"Stealth Mode: Enabled (Peer ID: {_currentPeerId})";
    }
} 