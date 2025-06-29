# BitTorrent Client (C#)

A modern, feature-rich BitTorrent client built with C# and WPF, implementing the full BitTorrent protocol specification.

## Key Technologies

- **C# 11 / .NET 8.0** — Core language and runtime
- **WPF** — Desktop UI framework (Windows only)
- **MVVM** — UI architecture pattern
- **System.Net.Sockets** — TCP networking for peer/tracker communication
- **System.Net.Http** — HTTP/HTTPS tracker requests
- **System.IO** — File and stream management
- **System.Threading** — Async operations and concurrency
- **Custom Bencode Parser** — For .torrent files and tracker responses
- **Custom Peer Wire Protocol Implementation** — For BitTorrent peer messaging
- **AI/Heuristic Peer Selection** — Adaptive, extensible peer ranking
- **Logging** — To file and console for debugging and troubleshooting
- **StealthMode** — Privacy features, peer ID masking, and tracker request obfuscation

## Features

- **Full BitTorrent Protocol Support**: Complete implementation of BEP 3 (BitTorrent Protocol)
- **Torrent File Parsing**: Parse and load .torrent files with metadata extraction
- **Tracker Communication**: HTTP/HTTPS tracker support with peer discovery
- **Peer Wire Protocol**: Full peer-to-peer communication with piece management
- **Modern WPF UI**: Clean, intuitive interface with real-time progress tracking
- **Console Mode**: Command-line interface for testing and automation
- **File Management**: Support for both single and multi-file torrents
- **Piece Verification**: SHA1 hash verification for data integrity
- **Progress Tracking**: Real-time download progress, speed, and ETA
- **Peer Management**: Connection management and peer statistics

## Requirements

- **.NET 8.0 SDK** or later
- **Windows 10/11** (for WPF UI)
- **Visual Studio 2022** or later (recommended)

## Quick Start

### 1. Clone and Build

```bash
git clone <repository-url>
cd BitTorrent-Client
```

### 2. Build the Project

**Using PowerShell (recommended):**
```powershell
.\build.ps1
```

**Using Batch file:**
```cmd
build.bat
```

**Using dotnet CLI:**
```bash
dotnet restore
dotnet build --configuration Release
```

### 3. Run the Application

**WPF Mode (GUI):**
```bash
dotnet run --project BitTorrentClient
```

**Console Mode (for testing):**
```bash
dotnet run --project BitTorrentClient -- --console
```

## Usage

### WPF Interface

1. **Add Torrent**: Click "Add Torrent" and select a .torrent file
2. **Select Download Location**: Choose where to save the files
3. **Start Download**: Click "Start" to begin downloading
4. **Monitor Progress**: View real-time progress, speed, and peer information
5. **Control Downloads**: Use Start/Stop/Pause/Remove buttons to manage torrents

### Console Mode

The console mode provides a simple interface for testing:

1. Enter the path to a .torrent file
2. Specify the download directory
3. Monitor real-time progress in the console
4. Press any key to stop the download

## Architecture

### Core Components

- **`TorrentParser.cs`**: Parses .torrent files and extracts metadata
- **`BencodeParser.cs`**: Handles Bencode encoding/decoding (BEP 3)
- **`TrackerClient.cs`**: Communicates with BitTorrent trackers
- **`PeerConnection.cs`**: Manages peer-to-peer connections
- **`PieceManager.cs`**: Handles piece verification and file assembly
- **`TorrentDownloader.cs`**: Orchestrates the entire download process

### UI Components

- **`MainWindow.xaml`**: Main application window
- **`TorrentViewModel.cs`**: MVVM view model for torrent management
- **`App.xaml`**: Application entry point

## 🧠 Intelligent Peer Selection (AI Module)

The client includes an **AI-powered peer selection module** (`Core/AI/IntelligentPeerSelector.cs`) that optimizes which peers to connect to and request pieces from. This module:

- **Scores and ranks peers** based on download speed, reliability, response time, and piece success rate.
- **Promotes or demotes peers** dynamically, blacklisting unreliable ones and prioritizing high-performing peers.
- **Adapts peer selection** in real time to maximize download speed and reliability.
- **Extensible for future ML integration** (e.g., reinforcement learning for peer selection).

This intelligent selection helps the client avoid slow or malicious peers and improves overall torrent performance.

## Protocol Implementation

### Supported Features

- ✅ **BEP 3**: BitTorrent Protocol
- ✅ **Bencode**: Data serialization format
- ✅ **Peer Wire Protocol**: Message types and handshaking
- ✅ **Tracker Protocol**: HTTP/HTTPS tracker communication
- ✅ **Piece Management**: Download, verification, and assembly
- ✅ **Choking Algorithm**: Basic peer selection
- 🔄 **Magnet Links**: Planned (BEP 9)
- 🔄 **DHT**: Planned (BEP 5)

### Message Types

- `choke` (0): Peer is choking the client
- `unchoke` (1): Peer is unchoking the client
- `interested` (2): Client is interested in peer
- `not_interested` (3): Client is not interested in peer
- `have` (4): Peer has a piece
- `bitfield` (5): Peer's piece availability
- `request` (6): Request a piece block
- `piece` (7): Piece block data
- `cancel` (8): Cancel a request

## Development

### Project Structure

```
BitTorrentClient/
├── Core/                    # Core protocol implementation
│   ├── TorrentParser.cs     # Torrent file parsing
│   ├── TrackerClient.cs     # Tracker communication
│   ├── PeerConnection.cs    # Peer wire protocol
│   ├── PieceManager.cs      # Piece management
│   └── Downloader.cs        # Main download orchestrator
├── ViewModels/              # MVVM view models
│   └── TorrentViewModel.cs  # Torrent management
├── Tests/                   # Unit tests
│   └── TorrentParserTests.cs
├── MainWindow.xaml          # Main UI
├── App.xaml                 # Application entry
└── Program.cs               # Program entry point
```

### Building from Source

1. **Install Prerequisites**:
   - .NET 8.0 SDK
   - Visual Studio 2022 (optional)

2. **Clone Repository**:
   ```bash
   git clone <repository-url>
   cd BitTorrent-Client
   ```

3. **Restore Dependencies**:
   ```bash
   dotnet restore
   ```

4. **Build Project**:
   ```bash
   dotnet build --configuration Release
   ```

5. **Run Tests**:
   ```bash
   dotnet test
   ```

### Adding Features

The project is designed with extensibility in mind:

- **New Protocol Features**: Add to the Core namespace
- **UI Enhancements**: Modify WPF files in the root
- **Additional Formats**: Extend the parser classes

## Testing

### Unit Tests

Run the test suite:
```bash
dotnet test
```

### Manual Testing

1. **Console Mode**: Test core functionality
   ```bash
   dotnet run --project BitTorrentClient -- --console
   ```

2. **WPF Mode**: Test user interface
   ```bash
   dotnet run --project BitTorrentClient
   ```

### Test Torrents

For testing, you can use:
- Ubuntu ISO torrents (legal and reliable)
- Open source software distributions
- Public domain content

## Troubleshooting

### Common Issues

1. **Build Errors**:
   - Ensure .NET 8.0 SDK is installed
   - Run `dotnet restore` before building
   - Check for missing dependencies

2. **Network Issues**:
   - Verify firewall settings
   - Check port 6881 availability
   - Ensure tracker URLs are accessible

3. **Permission Errors**:
   - Run as administrator if needed
   - Check download directory permissions

### Debug Mode

Enable detailed logging:
```bash
dotnet run --project BitTorrentClient --configuration Debug
```

## Default Port

- The client listens on port **6882** by default (previously 6881). You can change this in `TorrentDownloader` and `StealthMode` if needed.

## Logging

- The client writes debug logs to `debug.log` in the working directory and also outputs logs to the console.
- If you do not see the log file, check the console output or search your user folder for `debug.log`.
- Log messages include `[DEBUG]`, `[PEER]`, and `[DOWNLOADER]` for detailed protocol and connection information.

## Troubleshooting Peer Connections

If you see `Connected Peers: 0/0` and no download progress:

- Check the console for `[PEER]` and `[DOWNLOADER]` log messages.
- Make sure your firewall allows inbound and outbound connections on port 6882.
- Try a different torrent with a healthy swarm.
- If all peer connections time out, the tracker may be returning stale or unreachable peers.
- The log file may not update if the app is run from a different working directory. Try searching your user folder for `debug.log`.

## Known Issues

- Some ISPs block BitTorrent ports (6881-6889). Try changing the port if you have connectivity issues.
- The log file may not update if the app is run from a different working directory.
- The client currently does not support DHT or magnet links (planned features).

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- BitTorrent Protocol Specification (BEP 3)
- .NET Community for excellent tooling
- Open source community for inspiration

## Roadmap

- [ ] Magnet link support (BEP 9)
- [ ] Distributed Hash Table (BEP 5)
- [ ] Peer Exchange (BEP 11)
- [ ] IPv6 support
- [ ] Bandwidth limiting
- [ ] Encryption (BEP 7)
- [ ] Web seed support (BEP 19)
- [ ] Cross-platform UI (Avalonia) 