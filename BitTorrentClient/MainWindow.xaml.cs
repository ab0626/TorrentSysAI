using System.Windows;
using BitTorrentClient.ViewModels;

namespace BitTorrentClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
} 