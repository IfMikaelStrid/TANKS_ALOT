using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns one networked tank per connected client at the next free PlayerStart.
/// Place ONE instance per gameplay scene. Assign tankPrefab (must have NetworkObject + NetworkTransform).
/// </summary>
public class NetworkPlayerSpawner : NetworkBehaviour
{
    public GameObject tankPrefab;

    readonly Dictionary<ulong, NetworkObject> spawned = new Dictionary<ulong, NetworkObject>();
    readonly Dictionary<ulong, PlayerStart> assignments = new Dictionary<ulong, PlayerStart>();
    PlayerStart[] starts;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        starts = FindObjectsByType<PlayerStart>(FindObjectsSortMode.InstanceID);
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

        // Spawn for already-connected clients (host + any that connected before scene loaded).
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            SpawnFor(clientId);
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    void HandleClientConnected(ulong clientId) => SpawnFor(clientId);

    void HandleClientDisconnected(ulong clientId)
    {
        if (spawned.TryGetValue(clientId, out var no) && no != null)
            no.Despawn(true);
        spawned.Remove(clientId);
        assignments.Remove(clientId);
    }

    void SpawnFor(ulong clientId)
    {
        if (!IsServer) return;
        if (spawned.ContainsKey(clientId)) return;
        if (tankPrefab == null) { Debug.LogError("[NetworkPlayerSpawner] tankPrefab not assigned."); return; }

        if (NetworkManager.Singleton.ConnectedClients.Count > RelayBootstrap.MaxPlayers)
        {
            Debug.LogWarning($"[NetworkPlayerSpawner] Player cap ({RelayBootstrap.MaxPlayers}) reached, kicking {clientId}.");
            NetworkManager.Singleton.DisconnectClient(clientId);
            return;
        }

        PlayerStart start = NextFreeStart();
        if (start == null) { Debug.LogError("[NetworkPlayerSpawner] No free PlayerStart."); return; }

        Vector3 pos = start.transform.position + Vector3.up * start.spawnHeightOffset;
        GameObject go = Instantiate(tankPrefab, pos, start.transform.rotation);
        go.name = $"Tank_Player{start.playerNumber}";

        var listener = go.GetComponent<InputListener>();
        if (listener != null)
        {
            listener.playerNumber = start.playerNumber;
            listener.moveSpeed = start.moveSpeed;
            listener.rotateSpeed = start.rotateSpeed;
            listener.tankColor = start.tankColor;
        }

        var no = go.GetComponent<NetworkObject>();
        if (no == null) { Debug.LogError("[NetworkPlayerSpawner] tankPrefab missing NetworkObject."); Destroy(go); return; }
        no.SpawnWithOwnership(clientId, true);

        spawned[clientId] = no;
        assignments[clientId] = start;
    }

    PlayerStart NextFreeStart()
    {
        var taken = new HashSet<int>();
        foreach (var ps in assignments.Values) if (ps != null) taken.Add(ps.playerNumber);
        foreach (var ps in starts) if (ps != null && !taken.Contains(ps.playerNumber)) return ps;
        return null;
    }

    public IReadOnlyDictionary<ulong, NetworkObject> SpawnedTanks => spawned;
}
