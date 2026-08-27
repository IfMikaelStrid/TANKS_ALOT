using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click wiring for the networking phase-1 setup: prepares the tank prefab,
/// registers it as a network prefab and creates the NetworkManager in the open scene.
/// </summary>
public static class TanksNetworkSetup
{
    const string TankPrefabPath = "Assets/Mesh/tank/Prefab/TomThanks.prefab";
    const string PrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Tools/TANKS/Setup Networking")]
    public static void Run()
    {
        var tankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefabPath);
        if (tankPrefab == null)
        {
            Debug.LogError($"[TanksNetworkSetup] Tank prefab not found at {TankPrefabPath}.");
            return;
        }

        PrepareTankPrefab(TankPrefabPath);
        var prefabsList = RegisterNetworkPrefab(TankPrefabPath);
        ConfigureScene(tankPrefab, prefabsList);

        AssetDatabase.SaveAssets();
        Debug.Log("[TanksNetworkSetup] Done. Save the scene, then press Play and use Host / Join.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Prefab
    // ═══════════════════════════════════════════════════════════════

    static void PrepareTankPrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            EnsureComponent<NetworkObject>(root);
            EnsureComponent<InputListener>(root);
            EnsureComponent<TankNetworkIdentity>(root);

            var transform = EnsureComponent<NetworkTransform>(root);
            transform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            transform.Interpolate = true;

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        var component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Prefab list
    // ═══════════════════════════════════════════════════════════════

    static NetworkPrefabsList RegisterNetworkPrefab(string path)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabsListPath);
        if (list == null)
        {
            Debug.LogError($"[TanksNetworkSetup] Network prefabs list not found at {PrefabsListPath}.");
            return null;
        }

        // The list ships with an entry pointing at a prefab that no longer exists.
        var stale = new List<NetworkPrefab>();
        foreach (var entry in list.PrefabList)
        {
            if (entry == null || entry.Prefab == null)
                stale.Add(entry);
        }
        foreach (var entry in stale)
            list.Remove(entry);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null && !list.Contains(prefab))
            list.Add(new NetworkPrefab { Prefab = prefab });

        EditorUtility.SetDirty(list);
        return list;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scene
    // ═══════════════════════════════════════════════════════════════

    static void ConfigureScene(GameObject tankPrefab, NetworkPrefabsList prefabsList)
    {
        var manager = Object.FindFirstObjectByType<NetworkManager>();
        if (manager == null)
        {
            var host = new GameObject("NetworkManager");
            manager = host.AddComponent<NetworkManager>();
        }

        var transport = EnsureComponent<UnityTransport>(manager.gameObject);
        var spawner = EnsureComponent<TankSpawnManager>(manager.gameObject);
        EnsureComponent<NetworkConnectUI>(manager.gameObject);

        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.ConnectionApproval = true;
        manager.NetworkConfig.PlayerPrefab = null;

        if (prefabsList != null && !manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabsList))
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabsList);

        spawner.tankPrefab = tankPrefab;

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
    }
}
