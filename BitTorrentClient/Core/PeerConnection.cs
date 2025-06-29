using System.Net.Sockets;
using System.Text;
using BitTorrentClient.Core.Privacy;
using System.Collections;
using System.Net;

namespace BitTorrentClient.Core;

public enum PeerState
{
    Disconnected,
    Connecting,
    Handshaking,
    Connected,
    Choked,
    Interested
}

public class PeerConnection : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly string _peerId;
    private readonly string _infoHash;
    private readonly int _port;
    private readonly StealthMode? _stealthMode;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    public string Ip { get; }
    public int Port { get; }
    public PeerState State { get; private set; }
    public bool IsChoked { get; private set; } = true;
    public bool IsInterested { get; private set; } = false;
    public bool HasPiece { get; private set; } = false;
    public BitArray? Bitfield { get; private set; }
    
    public event EventHandler<PeerMessage>? MessageReceived;
    public event EventHandler<Exception>? ConnectionError;
    public event EventHandler? ConnectionClosed;

    private static readonly string DebugLogPath = "debug.log";
    private static void LogDebug(string message)
    {
        var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(logLine);
        try { System.IO.File.AppendAllText(DebugLogPath, logLine + "\n"); } catch { }
    }

    public PeerConnection(string ip, int port, string peerId, string infoHash, int listenPort, StealthMode? stealthMode = null)
    {
        Ip = ip;
        Port = port;
        _peerId = peerId;
        _infoHash = infoHash;
        _port = listenPort;
        _stealthMode = stealthMode;
        _cancellationTokenSource = new CancellationTokenSource();
        State = PeerState.Disconnected;
    }

    public async Task ConnectAsync()
    {
        try
        {
            LogDebug($"[PEER] Connecting to {Ip}:{Port}");
            State = PeerState.Connecting;
            _tcpClient = new TcpClient();
            
            // Set connection timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10 second timeout
            await _tcpClient.ConnectAsync(Ip, Port, cts.Token);
            
            _stream = _tcpClient.GetStream();
            
            LogDebug($"[PEER] TCP connection established to {Ip}:{Port}");
            State = PeerState.Handshaking;
            await PerformHandshakeAsync();
            
            LogDebug($"[PEER] Handshake completed successfully with {Ip}:{Port}");
            State = PeerState.Connected;
            _ = Task.Run(ReceiveLoopAsync);
        }
        catch (OperationCanceledException)
        {
            LogDebug($"[PEER] Connection timeout to {Ip}:{Port}");
            State = PeerState.Disconnected;
            ConnectionError?.Invoke(this, new TimeoutException($"Connection timeout to {Ip}:{Port}"));
            throw;
        }
        catch (Exception ex)
        {
            LogDebug($"[PEER] Connection failed to {Ip}:{Port}: {ex.Message}");
            State = PeerState.Disconnected;
            ConnectionError?.Invoke(this, ex);
            throw;
        }
    }

    private async Task PerformHandshakeAsync()
    {
        try
        {
            var handshake = new byte[68];
            handshake[0] = 19; // Protocol string length
            Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(handshake, 1);
            
            // 8 reserved bytes (all zeros)
            for (int i = 20; i < 28; i++)
                handshake[i] = 0;
            
            // Info hash
            var infoHashBytes = Convert.FromHexString(_infoHash);
            infoHashBytes.CopyTo(handshake, 28);
            
            // Peer ID
            var peerIdBytes = Encoding.ASCII.GetBytes(_stealthMode != null && _stealthMode.IsEnabled ? _stealthMode.CurrentPeerId : _peerId);
            peerIdBytes.CopyTo(handshake, 48);

            LogDebug($"[PEER] Sending handshake to {Ip}:{Port}");
            LogDebug($"[PEER] Handshake details - InfoHash: {_infoHash}, PeerID: {(_stealthMode != null && _stealthMode.IsEnabled ? _stealthMode.CurrentPeerId : _peerId)}");
            LogDebug($"[PEER] Handshake bytes: {Convert.ToHexString(handshake)}");

            // Send handshake
            await _stream!.WriteAsync(handshake);
            await _stream.FlushAsync();
            
            LogDebug($"[PEER] Handshake sent, waiting for response from {Ip}:{Port}");
            
            // Receive handshake
            var response = new byte[68];
            var bytesRead = await _stream.ReadAsync(response);
            
            LogDebug($"[PEER] Received {bytesRead} bytes from {Ip}:{Port}");
            
            if (bytesRead != 68)
            {
                LogDebug($"[PEER] Invalid handshake response length: {bytesRead} (expected 68) from {Ip}:{Port}");
                throw new Exception($"Invalid handshake response length: {bytesRead} (expected 68)");
            }
            
            LogDebug($"[PEER] Response bytes: {Convert.ToHexString(response)}");
            
            // Verify protocol string
            var protocolLength = response[0];
            var protocolString = Encoding.ASCII.GetString(response, 1, protocolLength);
            LogDebug($"[PEER] Protocol string: '{protocolString}' (length: {protocolLength})");
            
            if (protocolString != "BitTorrent protocol")
            {
                LogDebug($"[PEER] Invalid protocol string: '{protocolString}' from {Ip}:{Port}");
                throw new Exception($"Invalid protocol string: '{protocolString}'");
            }
            
            // Verify info hash
            var responseInfoHash = Convert.ToHexString(response, 28, 20).ToLower();
            LogDebug($"[PEER] Response info hash: {responseInfoHash}");
            
            if (responseInfoHash != _infoHash)
            {
                LogDebug($"[PEER] Info hash mismatch: expected {_infoHash}, got {responseInfoHash} from {Ip}:{Port}");
                throw new Exception($"Info hash mismatch: expected {_infoHash}, got {responseInfoHash}");
            }
            
            // Extract peer ID from response
            var responsePeerId = Encoding.ASCII.GetString(response, 48, 20);
            LogDebug($"[PEER] Remote peer ID: {responsePeerId}");
            
            LogDebug($"[PEER] Handshake verification successful with {Ip}:{Port}");
        }
        catch (Exception ex)
        {
            LogDebug($"[PEER] Handshake failed with {Ip}:{Port}: {ex.Message}");
            throw;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            LogDebug($"[PEER] Starting receive loop for {Ip}:{Port}");
            while (!_cancellationTokenSource.Token.IsCancellationRequested && _stream != null)
            {
                var message = await ReadMessageAsync();
                if (message != null)
                {
                    LogDebug($"[PEER] Received message from {Ip}:{Port}: {message.MessageType}");
                    ProcessMessage(message);
                    MessageReceived?.Invoke(this, message);
                }
            }
        }
        catch (Exception ex)
        {
            LogDebug($"[PEER] Receive loop error for {Ip}:{Port}: {ex.Message}");
            ConnectionError?.Invoke(this, ex);
        }
        finally
        {
            LogDebug($"[PEER] Receive loop ended for {Ip}:{Port}");
            State = PeerState.Disconnected;
            ConnectionClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<PeerMessage?> ReadMessageAsync()
    {
        if (_stream == null) return null;

        try
        {
            // Read message length (4 bytes)
            var lengthBuffer = new byte[4];
            var bytesRead = await _stream.ReadAsync(lengthBuffer);
            if (bytesRead != 4) 
            {
                LogDebug($"[PEER] Failed to read message length from {Ip}:{Port}, got {bytesRead} bytes");
                return null;
            }

            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBuffer);
            
            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            LogDebug($"[PEER] Message length from {Ip}:{Port}: {messageLength}");
            
            if (messageLength == 0)
                return new PeerMessage { MessageType = PeerMessageType.KeepAlive };
            
            if (messageLength > 1024 * 1024) // 1MB limit
            {
                LogDebug($"[PEER] Message too large from {Ip}:{Port}: {messageLength} bytes");
                return null;
            }
            
            // Read message type and payload
            var messageBuffer = new byte[messageLength];
            bytesRead = await _stream.ReadAsync(messageBuffer);
            if (bytesRead != messageLength) 
            {
                LogDebug($"[PEER] Failed to read message payload from {Ip}:{Port}, got {bytesRead}/{messageLength} bytes");
                return null;
            }

            var messageType = (PeerMessageType)messageBuffer[0];
            var payload = new byte[messageLength - 1];
            Array.Copy(messageBuffer, 1, payload, 0, payload.Length);

            LogDebug($"[PEER] Message from {Ip}:{Port}: Type={messageType}, PayloadLength={payload.Length}");

            return new PeerMessage
            {
                MessageType = messageType,
                Payload = payload
            };
        }
        catch (Exception ex)
        {
            LogDebug($"[PEER] Error reading message from {Ip}:{Port}: {ex.Message}");
            return null;
        }
    }

    private void ProcessMessage(PeerMessage message)
    {
        switch (message.MessageType)
        {
            case PeerMessageType.Choke:
                LogDebug($"[PEER] {Ip}:{Port} choked us");
                IsChoked = true;
                break;
            case PeerMessageType.Unchoke:
                LogDebug($"[PEER] {Ip}:{Port} unchoked us");
                IsChoked = false;
                break;
            case PeerMessageType.Interested:
                LogDebug($"[PEER] {Ip}:{Port} is interested");
                IsInterested = true;
                break;
            case PeerMessageType.NotInterested:
                LogDebug($"[PEER] {Ip}:{Port} is not interested");
                IsInterested = false;
                break;
            case PeerMessageType.Have:
                if (message.Payload?.Length == 4)
                {
                    var pieceIndex = BitConverter.ToInt32(message.Payload, 0);
                    if (BitConverter.IsLittleEndian)
                        pieceIndex = IPAddress.NetworkToHostOrder(pieceIndex);
                    
                    LogDebug($"[PEER] {Ip}:{Port} has piece {pieceIndex}");
                    
                    if (Bitfield != null && pieceIndex < Bitfield.Length)
                        Bitfield[pieceIndex] = true;
                }
                break;
            case PeerMessageType.Bitfield:
                LogDebug($"[PEER] {Ip}:{Port} sent bitfield, length: {message.Payload?.Length ?? 0}");
                if (message.Payload != null)
                {
                    Bitfield = new BitArray(message.Payload);
                    LogDebug($"[PEER] {Ip}:{Port} bitfield: {string.Join("", Bitfield.Cast<bool>().Take(20).Select(b => b ? "1" : "0"))}...");
                }
                break;
            case PeerMessageType.Request:
                LogDebug($"[PEER] {Ip}:{Port} requested piece data");
                break;
            case PeerMessageType.Piece:
                LogDebug($"[PEER] {Ip}:{Port} sent piece data, size: {message.Payload?.Length ?? 0}");
                break;
            case PeerMessageType.Cancel:
                LogDebug($"[PEER] {Ip}:{Port} cancelled request");
                break;
            case PeerMessageType.KeepAlive:
                LogDebug($"[PEER] {Ip}:{Port} keep-alive");
                break;
            default:
                LogDebug($"[PEER] {Ip}:{Port} unknown message type: {message.MessageType}");
                break;
        }
    }

    public async Task SendMessageAsync(PeerMessage message)
    {
        if (_stream == null) return;

        var messageBytes = EncodeMessage(message);
        LogDebug($"[PEER] Sending message to {Ip}:{Port}: {message.MessageType}, PayloadLength={message.Payload?.Length ?? 0}");
        await _stream.WriteAsync(messageBytes);
        await _stream.FlushAsync();
    }

    private byte[] EncodeMessage(PeerMessage message)
    {
        var payloadLength = message.Payload?.Length ?? 0;
        var totalLength = 4 + 1 + payloadLength; // length + type + payload
        
        var result = new byte[totalLength];
        
        // Message length (4 bytes)
        var lengthBytes = BitConverter.GetBytes(payloadLength + 1);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        lengthBytes.CopyTo(result, 0);
        
        // Message type (1 byte)
        result[4] = (byte)message.MessageType;
        
        // Payload
        if (message.Payload != null)
            message.Payload.CopyTo(result, 5);
        
        return result;
    }

    public async Task SendChokeAsync()
    {
        LogDebug($"[PEER] Sending choke to {Ip}:{Port}");
        await SendMessageAsync(new PeerMessage { MessageType = PeerMessageType.Choke });
    }

    public async Task SendUnchokeAsync()
    {
        LogDebug($"[PEER] Sending unchoke to {Ip}:{Port}");
        await SendMessageAsync(new PeerMessage { MessageType = PeerMessageType.Unchoke });
    }

    public async Task SendInterestedAsync()
    {
        LogDebug($"[PEER] Sending interested to {Ip}:{Port}");
        await SendMessageAsync(new PeerMessage { MessageType = PeerMessageType.Interested });
    }

    public async Task SendNotInterestedAsync()
    {
        LogDebug($"[PEER] Sending not interested to {Ip}:{Port}");
        await SendMessageAsync(new PeerMessage { MessageType = PeerMessageType.NotInterested });
    }

    public async Task SendHaveAsync(int pieceIndex)
    {
        LogDebug($"[PEER] Sending have piece {pieceIndex} to {Ip}:{Port}");
        var payload = BitConverter.GetBytes(pieceIndex);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(payload);
        
        await SendMessageAsync(new PeerMessage
        {
            MessageType = PeerMessageType.Have,
            Payload = payload
        });
    }

    public async Task SendBitfieldAsync(BitArray bitfield)
    {
        LogDebug($"[PEER] Sending bitfield to {Ip}:{Port}, length: {bitfield.Length}");
        var bytes = new byte[(bitfield.Length + 7) / 8];
        bitfield.CopyTo(bytes, 0);
        
        await SendMessageAsync(new PeerMessage
        {
            MessageType = PeerMessageType.Bitfield,
            Payload = bytes
        });
    }

    public async Task SendRequestAsync(int pieceIndex, int offset, int length)
    {
        LogDebug($"[PEER] Sending request to {Ip}:{Port}: piece={pieceIndex}, offset={offset}, length={length}");
        var payload = new byte[12];
        var pieceBytes = BitConverter.GetBytes(pieceIndex);
        var offsetBytes = BitConverter.GetBytes(offset);
        var lengthBytes = BitConverter.GetBytes(length);
        
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(pieceBytes);
            Array.Reverse(offsetBytes);
            Array.Reverse(lengthBytes);
        }
        
        pieceBytes.CopyTo(payload, 0);
        offsetBytes.CopyTo(payload, 4);
        lengthBytes.CopyTo(payload, 8);
        
        await SendMessageAsync(new PeerMessage
        {
            MessageType = PeerMessageType.Request,
            Payload = payload
        });
    }

    public async Task SendPieceAsync(int pieceIndex, int offset, byte[] data)
    {
        LogDebug($"[PEER] Sending piece to {Ip}:{Port}: piece={pieceIndex}, offset={offset}, dataSize={data.Length}");
        var payload = new byte[8 + data.Length];
        var pieceBytes = BitConverter.GetBytes(pieceIndex);
        var offsetBytes = BitConverter.GetBytes(offset);
        
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(pieceBytes);
            Array.Reverse(offsetBytes);
        }
        
        pieceBytes.CopyTo(payload, 0);
        offsetBytes.CopyTo(payload, 4);
        data.CopyTo(payload, 8);
        
        await SendMessageAsync(new PeerMessage
        {
            MessageType = PeerMessageType.Piece,
            Payload = payload
        });
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _cancellationTokenSource.Dispose();
    }
}

public class PeerMessage
{
    public PeerMessageType MessageType { get; set; }
    public byte[]? Payload { get; set; }
}

public enum PeerMessageType : byte
{
    Choke = 0,
    Unchoke = 1,
    Interested = 2,
    NotInterested = 3,
    Have = 4,
    Bitfield = 5,
    Request = 6,
    Piece = 7,
    Cancel = 8,
    KeepAlive = 255
} 