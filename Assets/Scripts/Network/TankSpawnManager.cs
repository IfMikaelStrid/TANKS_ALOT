using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side authority for player slots and tank spawning. Lives on the NetworkManager
/// object. Each connected client is given the lowest free slot (1..maxPlayers) and an owned
/// tank spawned at the matching PlayerStart.
/// </summary>
[RequireComponent(typeof(NetworkManager))]
[DisallowMultipleComponent]
public class TankSpawnManager : MonoBehaviour
{
    [Tooltip("Must have a NetworkObject and be registered in the network prefabs list.")]
    public GameObject tankPrefab;

    [Tooltip("Connections beyond this count are rejected.")]
    [Min(1)]
    public int maxPlayers = 4;

    NetworkManager m_NetworkManager;

    readonly Dictionary<ulong, int> m_SlotByClient = new Dictionary<ulong, int>();
    readonly Dictionary<ulong, NetworkObject> m_TankByClient = new Dictionary<ulong, NetworkObject>();

    void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();

        // Runs before any PlayerStart.Start(), which suppresses the offline auto-spawn.
        PlayerStart.NetworkManaged = true;

        m_NetworkManager.NetworkConfig.ConnectionApproval = true;
    }

    void OnEnable()
    {
        // Netcode allows exactly one approval callback, so this is assigned rather than combined.
        m_NetworkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        m_NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        m_NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        m_NetworkManager.OnServerStopped += HandleServerStopped;
    }

    void OnDisable()
    {
        if (m_NetworkManager == null) return;

        if (m_NetworkManager.ConnectionApprovalCallback == HandleConnectionApproval)
            m_NetworkManager.ConnectionApprovalCallback = null;

        m_NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        m_NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        m_NetworkManager.OnServerStopped -= HandleServerStopped;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Connection handling (server only)
    // ═══════════════════════════════════════════════════════════════

    void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                                  NetworkManager.ConnectionApprovalResponse response)
    {
        // Tanks are spawned explicitly below so we can control slot and spawn point.
        response.CreatePlayerObject = false;
        response.Pending = false;

        if (m_SlotByClient.Count >= maxPlayers)
        {
            response.Approved = false;
            response.Reason = "Server is full.";
            return;
        }

        response.Approved = true;
    }

    void HandleClientConnected(ulong clientId)
    {
        if (!m_NetworkManager.IsServer) return;
        SpawnTankFor(clientId);
    }

    void HandleClientDisconnected(ulong clientId)
    {
        if (!m_NetworkManager.IsServer) return;

        if (m_TankByClient.TryGetValue(clientId, out var tank))
        {
            if (tank != null && tank.IsSpawned)
                tank.Despawn(true);

            m_TankByClient.Remove(clientId);
        }

        m_SlotByClient.Remove(clientId);
    }

    void HandleServerStopped(bool wasHost)
    {
        m_TankByClient.Clear();
        m_SlotByClient.Clear();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Spawning
    // ═══════════════════════════════════════════════════════════════

    void SpawnTankFor(ulong clientId)
    {
        if (m_TankByClient.ContainsKey(clientId)) return;

        if (tankPrefab == null)
        {
            Debug.LogError("[TankSpawnManager] No tank prefab assigned — run Tools/TANKS/Setup Networking.");
            return;
        }

        int slot = AllocateSlot(clientId);
        if (slot < 1)
        {
            Debug.LogWarning($"[TankSpawnManager] No free slot for client {clientId}.");
            return;
        }

        PlayerStart spawnPoint = ResolveSpawnPoint(slot);
        Vector3 position = spawnPoint != null ? spawnPoint.SpawnPosition : Vector3.up;
        Quaternion rotation = spawnPoint != null ? spawnPoint.SpawnRotation : Quaternion.identity;

        GameObject tank = Instantiate(tankPrefab, position, rotation);

        var netObject = tank.GetComponent<NetworkObject>();
        if (netObject == null)
        {
            Debug.LogError("[TankSpawnManager] Tank prefab has no NetworkObject — run Tools/TANKS/Setup Networking.");
            Destroy(tank);
            m_SlotByClient.Remove(clientId);
            return;
        }

        netObject.SpawnWithOwnership(clientId);

        var identity = tank.GetComponent<TankNetworkIdentity>();
        if (identity != null)
            identity.AssignPlayerNumber(slot);

        m_TankByClient[clientId] = netObject;

        Debug.Log($"[TankSpawnManager] Spawned tank for client {clientId} as player {slot}.");
    }

    int AllocateSlot(ulong clientId)
    {
        if (m_SlotByClient.TryGetValue(clientId, out int existing))
            return existing;

        for (int slot = 1; slot <= maxPlayers; slot++)
        {
            if (m_SlotByClient.ContainsValue(slot)) continue;

            m_SlotByClient[clientId] = slot;
            return slot;
        }

        return -1;
    }

    static PlayerStart ResolveSpawnPoint(int slot)
    {
        var points = FindObjectsByType<PlayerStart>(FindObjectsSortMode.None);
        if (points.Length == 0) return null;

        foreach (var point in points)
        {
            if (point.playerNumber == slot)
                return point;
        }

        // Fall back to a stable ordering so every slot still gets a distinct point.
        System.Array.Sort(points, (a, b) => string.CompareOrdinal(a.name, b.name));
        return points[(slot - 1) % points.Length];
    }
}
