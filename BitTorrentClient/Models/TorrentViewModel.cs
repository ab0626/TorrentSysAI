using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BitTorrentClient.Core;

namespace BitTorrentClient.Models;

public class TorrentViewModel : INotifyPropertyChanged
{
    private readonly TorrentDownloader _downloader;
    private TorrentStatus _status;
    private TorrentStats _stats = new();
    private string _name = string.Empty;
    private string _size = string.Empty;
    private string _progress = "0%";
    private string _downloadSpeed = "0 B/s";
    private string _uploadSpeed = "0 B/s";
    private string _eta = "--:--";
    private string _peers = "0/0";

    public TorrentViewModel(TorrentMetadata metadata, string downloadPath)
    {
        _downloader = new TorrentDownloader(metadata, downloadPath);
        _name = metadata.Info.Name;
        _size = FormatBytes(metadata.Info.TotalSize);

        // Subscribe to events
        _downloader.StatusChanged += OnStatusChanged;
        _downloader.StatsUpdated += OnStatsUpdated;
        _downloader.ErrorOccurred += OnErrorOccurred;

        // Initialize commands
        StartCommand = new RelayCommand(async () => await StartAsync(), () => CanStart());
        StopCommand = new RelayCommand(async () => await StopAsync(), () => CanStop());
        PauseCommand = new RelayCommand(async () => await PauseAsync(), () => CanPause());
        ResumeCommand = new RelayCommand(async () => await ResumeAsync(), () => CanResume());
        RemoveCommand = new RelayCommand(async () => await RemoveAsync(), () => CanRemove());
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    public string Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set => SetProperty(ref _downloadSpeed, value);
    }

    public string UploadSpeed
    {
        get => _uploadSpeed;
        set => SetProperty(ref _uploadSpeed, value);
    }

    public string ETA
    {
        get => _eta;
        set => SetProperty(ref _eta, value);
    }

    public string Peers
    {
        get => _peers;
        set => SetProperty(ref _peers, value);
    }

    public TorrentStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand RemoveCommand { get; }

    private async Task StartAsync()
    {
        try
        {
            await _downloader.StartAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _downloader.StopAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private async Task PauseAsync()
    {
        try
        {
            await _downloader.PauseAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private async Task ResumeAsync()
    {
        try
        {
            await _downloader.ResumeAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private async Task RemoveAsync()
    {
        try
        {
            await _downloader.StopAsync();
            // Remove from collection
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private bool CanStart()
    {
        return Status == TorrentStatus.Stopped || Status == TorrentStatus.Error;
    }

    private bool CanStop()
    {
        return Status == TorrentStatus.Downloading || Status == TorrentStatus.Seeding;
    }

    private bool CanPause()
    {
        return Status == TorrentStatus.Downloading || Status == TorrentStatus.Seeding;
    }

    private bool CanResume()
    {
        return Status == TorrentStatus.Paused;
    }

    private bool CanRemove()
    {
        return Status == TorrentStatus.Stopped || Status == TorrentStatus.Error;
    }

    private void OnStatusChanged(object? sender, TorrentStatus status)
    {
        Status = status;
        OnPropertyChanged(nameof(StartCommand));
        OnPropertyChanged(nameof(StopCommand));
        OnPropertyChanged(nameof(PauseCommand));
        OnPropertyChanged(nameof(ResumeCommand));
        OnPropertyChanged(nameof(RemoveCommand));
    }

    private void OnStatsUpdated(object? sender, TorrentStats stats)
    {
        _stats = stats;
        Progress = $"{stats.Progress:P1}";
        DownloadSpeed = FormatBytes((long)stats.DownloadSpeed) + "/s";
        UploadSpeed = FormatBytes((long)stats.UploadSpeed) + "/s";
        ETA = FormatTimeSpan(stats.EstimatedTime);
        Peers = $"{stats.ConnectedPeers}/{stats.TotalPeers}";
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        // Handle error - could show a message box or update status
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan == TimeSpan.Zero) return "--:--";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        return $"{timeSpan.Minutes}:{timeSpan.Seconds:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        await _execute();
    }
} 