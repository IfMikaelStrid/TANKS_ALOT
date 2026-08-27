using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Minimal development lobby: start a host/server or join one by IP.
/// Intended for LAN and local testing only — there is no authentication here.
/// Replace with Unity Relay + Lobby before exposing this to the internet.
/// </summary>
[DisallowMultipleComponent]
public class NetworkConnectUI : MonoBehaviour
{
    [Tooltip("Address a client connects to. The host always listens on all interfaces.")]
    public string address = "127.0.0.1";
    public ushort port = 7777;

    string m_AddressField;
    string m_PortField;
    string m_Status = "";

    void Awake()
    {
        m_AddressField = address;
        m_PortField = port.ToString();
    }

    void OnGUI()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null) return;

        GUILayout.BeginArea(new Rect(10f, 10f, 240f, 190f), GUI.skin.box);

        if (!manager.IsListening)
            DrawConnectControls(manager);
        else
            DrawSessionControls(manager);

        if (!string.IsNullOrEmpty(m_Status))
            GUILayout.Label(m_Status);

        GUILayout.EndArea();
    }

    void DrawConnectControls(NetworkManager manager)
    {
        GUILayout.Label("TANKS — Multiplayer");

        GUILayout.BeginHorizontal();
        GUILayout.Label("IP", GUILayout.Width(30f));
        m_AddressField = GUILayout.TextField(m_AddressField);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Port", GUILayout.Width(30f));
        m_PortField = GUILayout.TextField(m_PortField);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Host") && ApplyConnectionData(manager, listenOnAllInterfaces: true))
            Begin(manager.StartHost(), "host");

        if (GUILayout.Button("Join") && ApplyConnectionData(manager, listenOnAllInterfaces: false))
            Begin(manager.StartClient(), "client");

        if (GUILayout.Button("Dedicated Server") && ApplyConnectionData(manager, listenOnAllInterfaces: true))
            Begin(manager.StartServer(), "server");
    }

    void DrawSessionControls(NetworkManager manager)
    {
        string role = manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client";
        GUILayout.Label($"{role} — client id {manager.LocalClientId}");

        if (manager.IsServer)
            GUILayout.Label($"Connected: {manager.ConnectedClientsIds.Count}");

        if (GUILayout.Button("Disconnect"))
        {
            manager.Shutdown();
            m_Status = "Disconnected.";
        }
    }

    bool ApplyConnectionData(NetworkManager manager, bool listenOnAllInterfaces)
    {
        var transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            m_Status = "No UnityTransport on NetworkManager.";
            return false;
        }

        if (!ushort.TryParse(m_PortField, out ushort parsedPort) || parsedPort == 0)
        {
            m_Status = "Invalid port.";
            return false;
        }

        string parsedAddress = m_AddressField != null ? m_AddressField.Trim() : "";
        if (string.IsNullOrEmpty(parsedAddress))
        {
            m_Status = "Invalid address.";
            return false;
        }

        address = parsedAddress;
        port = parsedPort;

        transport.SetConnectionData(parsedAddress, parsedPort, listenOnAllInterfaces ? "0.0.0.0" : null);
        return true;
    }

    void Begin(bool started, string role)
    {
        m_Status = started ? $"Started as {role}." : $"Failed to start {role}.";
    }
}
