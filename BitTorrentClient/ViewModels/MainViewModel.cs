using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using BitTorrentClient.Core;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BitTorrentClient.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<TorrentViewModel> Torrents { get; } = new();
    public ICommand AddTorrentCommand { get; }

    private TorrentViewModel? _selectedTorrent;
    public TorrentViewModel? SelectedTorrent
    {
        get => _selectedTorrent;
        set { _selectedTorrent = value; OnPropertyChanged(); }
    }

    public MainViewModel()
    {
        AddTorrentCommand = new RelayCommand(async () => await AddTorrentAsync());
    }

    private async Task AddTorrentAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select Torrent File",
            Filter = "Torrent Files (*.torrent)|*.torrent|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                var metadata = TorrentParser.ParseTorrentFile(openFileDialog.FileName);
                // Use OpenFileDialog for folder selection (simulate by asking for a file in the target folder)
                var folderDialog = new OpenFileDialog
                {
                    Title = "Select Download Folder (pick any file in the folder)",
                    Filter = "All Files (*.*)|*.*",
                    CheckFileExists = false,
                    FileName = "Select this folder"
                };
                string downloadPath = string.Empty;
                if (folderDialog.ShowDialog() == true)
                {
                    downloadPath = System.IO.Path.GetDirectoryName(folderDialog.FileName);
                }
                if (!string.IsNullOrEmpty(downloadPath))
                {
                    var torrentViewModel = new TorrentViewModel(metadata, downloadPath);
                    Torrents.Add(torrentViewModel);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading torrent file: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
} 