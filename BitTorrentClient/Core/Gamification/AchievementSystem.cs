using System.Text.Json;
using System.IO;

namespace BitTorrentClient.Core.Gamification;

public class Achievement
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int Points { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAt { get; set; }
    public AchievementType Type { get; set; }
    public int RequiredValue { get; set; }
    public int CurrentValue { get; set; }
    public double Progress => Math.Min((double)CurrentValue / RequiredValue, 1.0);
}

public enum AchievementType
{
    DownloadSpeed,
    UploadRatio,
    TotalDownloads,
    TotalUploads,
    SeedingTime,
    PeerConnections,
    FileSize,
    Streak,
    Community,
    Special
}

public class AchievementSystem
{
    private readonly List<Achievement> _achievements = new();
    private readonly Dictionary<string, int> _stats = new();
    private readonly string _savePath;
    private int _totalPoints = 0;
    private int _level = 1;

    public event EventHandler<Achievement>? AchievementUnlocked;
    public event EventHandler<int>? LevelUp;
    public event EventHandler<Dictionary<string, int>>? StatsUpdated;

    public int TotalPoints => _totalPoints;
    public int Level => _level;
    public List<Achievement> Achievements => _achievements.ToList();

    public AchievementSystem(string savePath = "achievements.json")
    {
        _savePath = savePath;
        InitializeAchievements();
        LoadProgress();
    }

    private void InitializeAchievements()
    {
        // Speed Achievements
        _achievements.Add(new Achievement
        {
            Id = "speed_1",
            Name = "Speed Demon",
            Description = "Reach 1 MB/s download speed",
            Icon = "🚀",
            Points = 10,
            Type = AchievementType.DownloadSpeed,
            RequiredValue = 1024 * 1024 // 1 MB/s
        });

        _achievements.Add(new Achievement
        {
            Id = "speed_10",
            Name = "Lightning Fast",
            Description = "Reach 10 MB/s download speed",
            Icon = "⚡",
            Points = 50,
            Type = AchievementType.DownloadSpeed,
            RequiredValue = 10 * 1024 * 1024 // 10 MB/s
        });

        // Upload Ratio Achievements
        _achievements.Add(new Achievement
        {
            Id = "ratio_1",
            Name = "Good Citizen",
            Description = "Maintain 1.0 upload ratio",
            Icon = "👍",
            Points = 20,
            Type = AchievementType.UploadRatio,
            RequiredValue = 100 // 1.0 ratio * 100
        });

        _achievements.Add(new Achievement
        {
            Id = "ratio_2",
            Name = "Generous Seeder",
            Description = "Maintain 2.0 upload ratio",
            Icon = "🎁",
            Points = 100,
            Type = AchievementType.UploadRatio,
            RequiredValue = 200 // 2.0 ratio * 100
        });

        // Download Count Achievements
        _achievements.Add(new Achievement
        {
            Id = "downloads_10",
            Name = "Downloader",
            Description = "Complete 10 downloads",
            Icon = "📥",
            Points = 25,
            Type = AchievementType.TotalDownloads,
            RequiredValue = 10
        });

        _achievements.Add(new Achievement
        {
            Id = "downloads_100",
            Name = "Download Master",
            Description = "Complete 100 downloads",
            Icon = "📥📥",
            Points = 200,
            Type = AchievementType.TotalDownloads,
            RequiredValue = 100
        });

        // Seeding Time Achievements
        _achievements.Add(new Achievement
        {
            Id = "seed_1h",
            Name = "Seeder",
            Description = "Seed for 1 hour",
            Icon = "🌱",
            Points = 15,
            Type = AchievementType.SeedingTime,
            RequiredValue = 3600 // 1 hour in seconds
        });

        _achievements.Add(new Achievement
        {
            Id = "seed_24h",
            Name = "Dedicated Seeder",
            Description = "Seed for 24 hours",
            Icon = "🌱🌱",
            Points = 150,
            Type = AchievementType.SeedingTime,
            RequiredValue = 86400 // 24 hours in seconds
        });

        // Peer Connection Achievements
        _achievements.Add(new Achievement
        {
            Id = "peers_50",
            Name = "Social Butterfly",
            Description = "Connect to 50 peers simultaneously",
            Icon = "🦋",
            Points = 30,
            Type = AchievementType.PeerConnections,
            RequiredValue = 50
        });

        // File Size Achievements
        _achievements.Add(new Achievement
        {
            Id = "size_1gb",
            Name = "Big Downloader",
            Description = "Download a 1 GB file",
            Icon = "💾",
            Points = 20,
            Type = AchievementType.FileSize,
            RequiredValue = 1024 * 1024 * 1024 // 1 GB
        });

        // Streak Achievements
        _achievements.Add(new Achievement
        {
            Id = "streak_7",
            Name = "Week Warrior",
            Description = "Use the client for 7 consecutive days",
            Icon = "🔥",
            Points = 75,
            Type = AchievementType.Streak,
            RequiredValue = 7
        });

        // Special Achievements
        _achievements.Add(new Achievement
        {
            Id = "first_download",
            Name = "First Steps",
            Description = "Complete your first download",
            Icon = "🎯",
            Points = 5,
            Type = AchievementType.Special,
            RequiredValue = 1
        });

        _achievements.Add(new Achievement
        {
            Id = "stealth_mode",
            Name = "Ghost Protocol",
            Description = "Use stealth mode for the first time",
            Icon = "👻",
            Points = 25,
            Type = AchievementType.Special,
            RequiredValue = 1
        });
    }

    public void UpdateStat(string statName, int value)
    {
        _stats[statName] = value;
        CheckAchievements();
        StatsUpdated?.Invoke(this, _stats);
        SaveProgress();
    }

    public void IncrementStat(string statName, int increment = 1)
    {
        var currentValue = _stats.GetValueOrDefault(statName, 0);
        UpdateStat(statName, currentValue + increment);
    }

    private void CheckAchievements()
    {
        foreach (var achievement in _achievements.Where(a => !a.IsUnlocked))
        {
            var shouldUnlock = false;

            switch (achievement.Type)
            {
                case AchievementType.DownloadSpeed:
                    achievement.CurrentValue = _stats.GetValueOrDefault("max_download_speed", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.UploadRatio:
                    var uploaded = _stats.GetValueOrDefault("total_uploaded", 0);
                    var downloaded = _stats.GetValueOrDefault("total_downloaded", 1);
                    var ratio = (int)((double)uploaded / downloaded * 100);
                    achievement.CurrentValue = ratio;
                    shouldUnlock = ratio >= achievement.RequiredValue;
                    break;

                case AchievementType.TotalDownloads:
                    achievement.CurrentValue = _stats.GetValueOrDefault("total_downloads", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.TotalUploads:
                    achievement.CurrentValue = _stats.GetValueOrDefault("total_uploads", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.SeedingTime:
                    achievement.CurrentValue = _stats.GetValueOrDefault("total_seeding_time", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.PeerConnections:
                    achievement.CurrentValue = _stats.GetValueOrDefault("max_peers_connected", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.FileSize:
                    achievement.CurrentValue = _stats.GetValueOrDefault("largest_file_downloaded", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.Streak:
                    achievement.CurrentValue = _stats.GetValueOrDefault("consecutive_days", 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;

                case AchievementType.Special:
                    achievement.CurrentValue = _stats.GetValueOrDefault(achievement.Id, 0);
                    shouldUnlock = achievement.CurrentValue >= achievement.RequiredValue;
                    break;
            }

            if (shouldUnlock)
            {
                UnlockAchievement(achievement);
            }
        }
    }

    private void UnlockAchievement(Achievement achievement)
    {
        achievement.IsUnlocked = true;
        achievement.UnlockedAt = DateTime.Now;
        _totalPoints += achievement.Points;

        // Check for level up
        var newLevel = (_totalPoints / 100) + 1;
        if (newLevel > _level)
        {
            _level = newLevel;
            LevelUp?.Invoke(this, _level);
        }

        AchievementUnlocked?.Invoke(this, achievement);
    }

    public void UnlockSpecialAchievement(string achievementId)
    {
        IncrementStat(achievementId);
    }

    public List<Achievement> GetUnlockedAchievements()
    {
        return _achievements.Where(a => a.IsUnlocked).ToList();
    }

    public List<Achievement> GetLockedAchievements()
    {
        return _achievements.Where(a => !a.IsUnlocked).ToList();
    }

    public double GetCompletionPercentage()
    {
        if (_achievements.Count == 0) return 0;
        return (double)GetUnlockedAchievements().Count / _achievements.Count * 100;
    }

    public string GetLevelTitle()
    {
        return _level switch
        {
            1 => "Newbie",
            2 => "Beginner",
            3 => "Intermediate",
            4 => "Advanced",
            5 => "Expert",
            6 => "Master",
            7 => "Grandmaster",
            8 => "Legend",
            9 => "Mythic",
            10 => "Divine",
            _ => $"Level {_level}"
        };
    }

    private void SaveProgress()
    {
        try
        {
            var data = new
            {
                Stats = _stats,
                TotalPoints = _totalPoints,
                Level = _level,
                Achievements = _achievements.Select(a => new
                {
                    a.Id,
                    a.IsUnlocked,
                    a.UnlockedAt,
                    a.CurrentValue
                })
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_savePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save achievements: {ex.Message}");
        }
    }

    private void LoadProgress()
    {
        try
        {
            if (!File.Exists(_savePath)) return;

            var json = File.ReadAllText(_savePath);
            var data = JsonSerializer.Deserialize<JsonElement>(json);

            if (data.TryGetProperty("Stats", out var statsElement))
            {
                _stats.Clear();
                foreach (var stat in statsElement.EnumerateObject())
                {
                    _stats[stat.Name] = stat.Value.GetInt32();
                }
            }

            if (data.TryGetProperty("TotalPoints", out var pointsElement))
                _totalPoints = pointsElement.GetInt32();

            if (data.TryGetProperty("Level", out var levelElement))
                _level = levelElement.GetInt32();

            if (data.TryGetProperty("Achievements", out var achievementsElement))
            {
                foreach (var achievementData in achievementsElement.EnumerateArray())
                {
                    var id = achievementData.GetProperty("Id").GetString();
                    var achievement = _achievements.FirstOrDefault(a => a.Id == id);
                    if (achievement != null)
                    {
                        achievement.IsUnlocked = achievementData.GetProperty("IsUnlocked").GetBoolean();
                        if (achievement.IsUnlocked && achievementData.TryGetProperty("UnlockedAt", out var unlockedAtElement))
                        {
                            achievement.UnlockedAt = unlockedAtElement.GetDateTime();
                        }
                        if (achievementData.TryGetProperty("CurrentValue", out var currentValueElement))
                        {
                            achievement.CurrentValue = currentValueElement.GetInt32();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load achievements: {ex.Message}");
        }
    }

    public void ResetProgress()
    {
        _stats.Clear();
        _totalPoints = 0;
        _level = 1;
        
        foreach (var achievement in _achievements)
        {
            achievement.IsUnlocked = false;
            achievement.UnlockedAt = null;
            achievement.CurrentValue = 0;
        }

        SaveProgress();
    }
} 