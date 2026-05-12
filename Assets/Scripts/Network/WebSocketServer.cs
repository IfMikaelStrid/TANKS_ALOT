using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

/// <summary>
/// Lightweight WebSocket server for the TANKS host.
/// Uses a raw TcpListener with manual WebSocket handshake for maximum Unity compatibility.
/// All Unity-facing callbacks are queued to the main thread via a ConcurrentQueue.
/// </summary>
public class WebSocketServer : MonoBehaviour
{
    public static WebSocketServer Instance { get; private set; }

    [Header("Server Settings")]
    public int port = 9090;
    public int maxPlayers = 8;

    // ── state ──
    public string RoomCode { get; private set; }
    public bool IsRunning { get; private set; }

    readonly Dictionary<string, ConnectedClient> clients = new Dictionary<string, ConnectedClient>();
    readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    TcpListener tcpListener;
    CancellationTokenSource cts;
    Thread acceptThread;

    public event Action<string, string> OnMessageReceived;   // clientId, rawJson
    public event Action<string> OnClientConnected;            // clientId
    public event Action<string> OnClientDisconnected;         // clientId

    public class ConnectedClient
    {
        public string Id;
        public TcpClient Tcp;
        public NetworkStream Stream;
        public int PlayerNumber;
        public string PlayerName;
        public Color TankColor = Color.blue;
        public readonly object WriteLock = new object();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        StopServer();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // drain main-thread queue
        while (mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════

    public void StartServer()
    {
        if (IsRunning) return;

        RoomCode = GenerateRoomCode();
        cts = new CancellationTokenSource();

        tcpListener = new TcpListener(IPAddress.Any, port);
        tcpListener.Start();

        IsRunning = true;
        Debug.Log($"[WebSocketServer] Started on port {port} | Room Code: {RoomCode}");

        acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        acceptThread.Start();
    }

    public void StopServer()
    {
        if (!IsRunning) return;
        IsRunning = false;

        cts?.Cancel();
        try { tcpListener?.Stop(); } catch { /* ignore */ }

        lock (clients)
        {
            foreach (var c in clients.Values)
            {
                try { SendCloseFrame(c); } catch { /* ignore */ }
                try { c.Tcp?.Close(); } catch { /* ignore */ }
            }
            clients.Clear();
        }

        tcpListener = null;
        cts = null;
        Debug.Log("[WebSocketServer] Stopped.");
    }

    public void SendToClient(string clientId, string json)
    {
        ConnectedClient client;
        lock (clients)
        {
            if (!clients.TryGetValue(clientId, out client)) return;
        }
        SendFrame(client, json);
    }

    public void Broadcast(string json)
    {
        List<ConnectedClient> snapshot;
        lock (clients) { snapshot = clients.Values.ToList(); }
        foreach (var c in snapshot)
            SendFrame(c, json);
    }

    public void BroadcastExcept(string excludeClientId, string json)
    {
        List<ConnectedClient> snapshot;
        lock (clients) { snapshot = clients.Values.Where(c => c.Id != excludeClientId).ToList(); }
        foreach (var c in snapshot)
            SendFrame(c, json);
    }

    public ConnectedClient GetClient(string clientId)
    {
        lock (clients)
        {
            clients.TryGetValue(clientId, out var c);
            return c;
        }
    }

    public List<ConnectedClient> GetAllClients()
    {
        lock (clients) { return clients.Values.ToList(); }
    }

    public int ConnectedPlayerCount
    {
        get { lock (clients) { return clients.Count; } }
    }

    public string GetClientIdByPlayerNumber(int playerNumber)
    {
        lock (clients)
        {
            foreach (var kv in clients)
                if (kv.Value.PlayerNumber == playerNumber)
                    return kv.Key;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Accept loop (background thread)
    // ═══════════════════════════════════════════════════════════════

    void AcceptLoop()
    {
        while (IsRunning && !cts.IsCancellationRequested)
        {
            try
            {
                var tcp = tcpListener.AcceptTcpClient();
                var thread = new Thread(() => HandleClient(tcp)) { IsBackground = true };
                thread.Start();
            }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (IsRunning)
                    Debug.LogError($"[WebSocketServer] Accept error: {ex.Message}");
            }
        }
    }

    void HandleClient(TcpClient tcp)
    {
        NetworkStream stream = null;
        string clientId = Guid.NewGuid().ToString();

        try
        {
            stream = tcp.GetStream();

            // read HTTP upgrade request
            if (!PerformHandshake(stream))
            {
                tcp.Close();
                return;
            }

            var client = new ConnectedClient
            {
                Id = clientId,
                Tcp = tcp,
                Stream = stream,
                PlayerNumber = -1
            };

            lock (clients) { clients[clientId] = client; }

            mainThreadQueue.Enqueue(() =>
            {
                OnClientConnected?.Invoke(clientId);
                Debug.Log($"[WebSocketServer] Client connected: {clientId}");
            });

            ReceiveLoop(client);
        }
        catch (Exception ex)
        {
            if (IsRunning)
                Debug.LogError($"[WebSocketServer] Client handler error: {ex.Message}");
        }
        finally
        {
            RemoveClient(clientId);
            try { tcp?.Close(); } catch { /* ignore */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  WebSocket handshake (RFC 6455)
    // ═══════════════════════════════════════════════════════════════

    static bool PerformHandshake(NetworkStream stream)
    {
        // read request into a byte buffer until we hit \r\n\r\n
        var raw = new List<byte>(512);
        int endMarker = 0; // tracks consecutive \r\n\r\n bytes
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0) return false;
            raw.Add((byte)b);

            // track end-of-headers: \r\n\r\n = 13,10,13,10
            if ((endMarker == 0 && b == 13) ||
                (endMarker == 1 && b == 10) ||
                (endMarker == 2 && b == 13) ||
                (endMarker == 3 && b == 10))
                endMarker++;
            else
                endMarker = (b == 13) ? 1 : 0;

            if (endMarker == 4) break;
            if (raw.Count > 8192) return false;
        }

        string request = Encoding.ASCII.GetString(raw.ToArray());

        // extract Sec-WebSocket-Key by parsing header lines
        string key = null;
        string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                key = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("[WebSocketServer] No Sec-WebSocket-Key found in handshake request.");
            return false;
        }

        Debug.Log($"[WebSocketServer] Handshake key: [{key}] (len={key.Length})");

        string acceptKey = ComputeAcceptKey(key);
        Debug.Log($"[WebSocketServer] Accept key: [{acceptKey}]");

        // send upgrade response
        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + acceptKey + "\r\n" +
            "\r\n";

        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
        stream.Write(responseBytes, 0, responseBytes.Length);
        stream.Flush();
        return true;
    }

    static string ComputeAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-5AB5DC085B11";
        byte[] combined = Encoding.ASCII.GetBytes(key + magic);
        using (var sha1 = new SHA1CryptoServiceProvider())
        {
            byte[] hash = sha1.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  WebSocket frame reading (RFC 6455)
    // ═══════════════════════════════════════════════════════════════

    void ReceiveLoop(ConnectedClient client)
    {
        var stream = client.Stream;

        while (IsRunning && !cts.IsCancellationRequested && client.Tcp.Connected)
        {
            try
            {
                // read first 2 bytes
                byte[] header = ReadExact(stream, 2);
                if (header == null) break;

                int opcode = header[0] & 0x0F;
                bool masked = (header[1] & 0x80) != 0;
                long payloadLen = header[1] & 0x7F;

                if (payloadLen == 126)
                {
                    byte[] ext = ReadExact(stream, 2);
                    if (ext == null) break;
                    payloadLen = (ext[0] << 8) | ext[1];
                }
                else if (payloadLen == 127)
                {
                    byte[] ext = ReadExact(stream, 8);
                    if (ext == null) break;
                    payloadLen = 0;
                    for (int i = 0; i < 8; i++)
                        payloadLen = (payloadLen << 8) | ext[i];
                }

                byte[] maskKey = null;
                if (masked)
                {
                    maskKey = ReadExact(stream, 4);
                    if (maskKey == null) break;
                }

                byte[] payload = payloadLen > 0 ? ReadExact(stream, (int)payloadLen) : new byte[0];
                if (payload == null) break;

                // unmask
                if (masked && maskKey != null)
                {
                    for (int i = 0; i < payload.Length; i++)
                        payload[i] ^= maskKey[i % 4];
                }

                switch (opcode)
                {
                    case 0x1: // text
                        string text = Encoding.UTF8.GetString(payload);
                        mainThreadQueue.Enqueue(() => OnMessageReceived?.Invoke(client.Id, text));
                        break;

                    case 0x8: // close
                        SendCloseFrame(client);
                        return;

                    case 0x9: // ping → pong
                        SendRawFrame(client, 0xA, payload);
                        break;

                    case 0xA: // pong — ignore
                        break;
                }
            }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                if (IsRunning)
                    Debug.LogError($"[WebSocketServer] Receive error from {client.Id}: {ex.Message}");
                break;
            }
        }
    }

    static byte[] ReadExact(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0) return null; // connection closed
            offset += read;
        }
        return buffer;
    }

    // ═══════════════════════════════════════════════════════════════
    //  WebSocket frame writing
    // ═══════════════════════════════════════════════════════════════

    static void SendFrame(ConnectedClient client, string text)
    {
        byte[] payload = Encoding.UTF8.GetBytes(text);
        SendRawFrame(client, 0x1, payload);
    }

    static void SendRawFrame(ConnectedClient client, int opcode, byte[] payload)
    {
        try
        {
            using (var ms = new MemoryStream())
            {
                // FIN + opcode
                ms.WriteByte((byte)(0x80 | opcode));

                // payload length (server → client: unmasked)
                if (payload.Length < 126)
                {
                    ms.WriteByte((byte)payload.Length);
                }
                else if (payload.Length <= 65535)
                {
                    ms.WriteByte(126);
                    ms.WriteByte((byte)(payload.Length >> 8));
                    ms.WriteByte((byte)(payload.Length & 0xFF));
                }
                else
                {
                    ms.WriteByte(127);
                    long len = payload.Length;
                    for (int i = 7; i >= 0; i--)
                        ms.WriteByte((byte)((len >> (8 * i)) & 0xFF));
                }

                ms.Write(payload, 0, payload.Length);

                byte[] frame = ms.ToArray();
                lock (client.WriteLock)
                {
                    client.Stream.Write(frame, 0, frame.Length);
                    client.Stream.Flush();
                }
            }
        }
        catch (IOException) { /* client gone */ }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    static void SendCloseFrame(ConnectedClient client)
    {
        SendRawFrame(client, 0x8, new byte[] { 0x03, 0xE8 }); // 1000 normal closure
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    void RemoveClient(string clientId)
    {
        bool removed;
        lock (clients) { removed = clients.Remove(clientId); }
        if (removed)
        {
            mainThreadQueue.Enqueue(() =>
            {
                OnClientDisconnected?.Invoke(clientId);
                Debug.Log($"[WebSocketServer] Client disconnected: {clientId}");
            });
        }
    }

    static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng = new System.Random();
        var code = new char[4];
        for (int i = 0; i < 4; i++)
            code[i] = chars[rng.Next(chars.Length)];
        return new string(code);
    }
}
