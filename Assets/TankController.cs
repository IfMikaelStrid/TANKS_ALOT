using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TankController : MonoBehaviour
{
    [Header("Tank Prefab")]
    public GameObject tankPrefab;
    public Vector3 spawnOffset = new Vector3(0f, 5f, 0f);
    public Vector3 spawnEulerAngles = new Vector3(-90f, 0f, 0f);
    public float spawnSpacing = 8f;

    [Header("Session")]
    public string sessionName = "Tank Session";
    public int maxPlayers = 4;
    public bool autoStartHost;

    [Header("Script Window")]
    [TextArea(10, 24)]
    public string routineScript = "LOOP 4\nMOVE 4\nTURN 90\nWAIT 0.25\nEND";
    public bool loopScriptRoutine;
    public bool autoCompileScript = true;

    [Header("Debug UI")]
    public bool showDebugUi = true;
    [SerializeField]
    private string joinCodeInput = string.Empty;

    private readonly List<TankPlayerController.TankInstruction> compiledPreview = new List<TankPlayerController.TankInstruction>();
    private ISession currentSession;
    private bool isBusy;
    private string statusMessage = "Idle";
    private string scriptCompileMessage = "Script not compiled yet.";
    private string scriptPreviewText = "No preview available.";
    private bool scriptPreviewDirty = true;
    private Vector2 scriptPreviewScroll;

    void Awake()
    {
        EnsureNetworkManager();
        ConfigureNetworkManager();
        SubscribeNetworkEvents();
        TankPlayerController.LocalRoutineValidationResult += HandleRoutineValidationResult;
        CompileScriptPreview();
    }

    async void Start()
    {
        if (autoStartHost)
        {
            await HostAsync();
        }
    }

    void OnDestroy()
    {
        UnsubscribeNetworkEvents();
        TankPlayerController.LocalRoutineValidationResult -= HandleRoutineValidationResult;
    }

    public void HostFromUi()
    {
        _ = HostAsync();
    }

    public void JoinFromUi()
    {
        _ = JoinByCodeAsync(joinCodeInput);
    }

    public void LeaveFromUi()
    {
        _ = LeaveSessionAsync();
    }

    public void SendRoutineFromUi()
    {
        if (!TryGetLocalTank(out TankPlayerController tank))
        {
            statusMessage = "Player tank not ready yet.";
            return;
        }

        if (!CompileScriptPreview(tank.maxInstructionsPerRoutine))
        {
            statusMessage = scriptCompileMessage;
            return;
        }

        if (!tank.SubmitScript(routineScript, loopScriptRoutine, out string submitMessage))
        {
            statusMessage = submitMessage;
            return;
        }

        statusMessage = submitMessage;
    }

    async Task HostAsync()
    {
        if (isBusy || currentSession != null)
        {
            return;
        }

        if (!ConfigureNetworkManager())
        {
            return;
        }

        isBusy = true;
        statusMessage = "Starting host...";
        try
        {
            await EnsureSignedInAsync();
            if (MultiplayerService.Instance == null)
            {
                statusMessage = "Multiplayer service unavailable.";
                return;
            }

            SessionOptions options = new SessionOptions
            {
                Name = string.IsNullOrWhiteSpace(sessionName) ? "Tank Session" : sessionName.Trim(),
                MaxPlayers = Mathf.Max(2, maxPlayers),
                IsPrivate = false
            };

            options.WithRelayNetwork();
            currentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            statusMessage = $"Hosting - code: {currentSession.Code}";
            Debug.Log(statusMessage);
        }
        catch (SessionException e)
        {
            statusMessage = $"Host failed: {e.Message}";
            Debug.LogError($"Host failed ({e.Error}): {e.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    async Task JoinByCodeAsync(string sessionCode)
    {
        if (isBusy || currentSession != null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionCode))
        {
            statusMessage = "Enter a join code first.";
            return;
        }

        if (!ConfigureNetworkManager())
        {
            return;
        }

        isBusy = true;
        statusMessage = "Joining...";
        try
        {
            await EnsureSignedInAsync();
            if (MultiplayerService.Instance == null)
            {
                statusMessage = "Multiplayer service unavailable.";
                return;
            }

            currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(sessionCode.Trim().ToUpperInvariant(), new JoinSessionOptions());
            statusMessage = $"Joined '{currentSession.Name}'";
        }
        catch (SessionException e)
        {
            statusMessage = $"Join failed: {e.Message}";
            Debug.LogError($"Join failed ({e.Error}): {e.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    async Task LeaveSessionAsync()
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        statusMessage = "Leaving session...";
        try
        {
            if (currentSession != null)
            {
                await currentSession.LeaveAsync();
                currentSession = null;
            }

            if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
            {
                NetworkManager.Singleton.Shutdown();
            }

            statusMessage = "Session left.";
        }
        catch (SessionException e)
        {
            statusMessage = $"Leave failed: {e.Message}";
            Debug.LogError($"Leave failed ({e.Error}): {e.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    void HandleRoutineValidationResult(bool accepted, string message)
    {
        statusMessage = message;
        if (!accepted)
        {
            Debug.LogWarning(message);
        }
    }

    bool TryGetLocalTank(out TankPlayerController tank)
    {
        tank = null;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            return false;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(NetworkManager.Singleton.LocalClientId, out var localClient) ||
            localClient.PlayerObject == null)
        {
            return false;
        }

        tank = localClient.PlayerObject.GetComponent<TankPlayerController>();
        return tank != null;
    }

    bool CompileScriptPreview(int maxInstructions = 128)
    {
        if (!TankPlayerController.TryCompileScript(routineScript, maxInstructions, out List<TankPlayerController.TankInstruction> compiled, out string message))
        {
            compiledPreview.Clear();
            scriptCompileMessage = $"Compile error: {message}";
            scriptPreviewText = "No executable preview.";
            scriptPreviewDirty = false;
            return false;
        }

        compiledPreview.Clear();
        compiledPreview.AddRange(compiled);
        scriptCompileMessage = message;
        scriptPreviewText = BuildPreviewText(compiledPreview);
        scriptPreviewDirty = false;
        return true;
    }

    static string BuildPreviewText(IReadOnlyList<TankPlayerController.TankInstruction> instructions)
    {
        if (instructions == null || instructions.Count == 0)
        {
            return "No instructions.";
        }

        int previewCount = Mathf.Min(instructions.Count, 64);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < previewCount; i++)
        {
            builder.Append(i + 1)
                   .Append(". ")
                   .AppendLine(TankPlayerController.FormatInstruction(instructions[i]));
        }

        if (instructions.Count > previewCount)
        {
            builder.Append("... ")
                   .Append(instructions.Count - previewCount)
                   .AppendLine(" more instruction(s).");
        }

        return builder.ToString();
    }

    void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var connectedClient))
        {
            return;
        }

        NetworkObject playerObject = connectedClient.PlayerObject;
        if (playerObject == null)
        {
            return;
        }

        int spawnIndex = (int)clientId;
        Vector3 targetPos = transform.position + spawnOffset + new Vector3(spawnIndex * spawnSpacing, 0f, 0f);
        Quaternion targetRot = Quaternion.Euler(spawnEulerAngles);

        playerObject.transform.SetPositionAndRotation(targetPos, targetRot);

        TankPlayerController tankPlayer = playerObject.GetComponent<TankPlayerController>();
        if (tankPlayer != null)
        {
            tankPlayer.SetAuthoritativeState(targetPos, targetRot);
        }
    }

    void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            currentSession = null;
            statusMessage = "Disconnected from host.";
        }
    }

    NetworkManager EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null)
        {
            return NetworkManager.Singleton;
        }

        NetworkManager existingManager = FindFirstObjectByType<NetworkManager>();
        if (existingManager != null)
        {
            return existingManager;
        }

        GameObject managerObject = new GameObject("NetworkManager");
        DontDestroyOnLoad(managerObject);
        managerObject.AddComponent<UnityTransport>();
        return managerObject.AddComponent<NetworkManager>();
    }

    bool ConfigureNetworkManager()
    {
        if (tankPrefab == null)
        {
            statusMessage = "Assign tankPrefab on TankController.";
            Debug.LogError(statusMessage);
            return false;
        }

        GameObject resolvedPrefab = ResolveNetworkReadyPrefab(tankPrefab);
        if (resolvedPrefab == null)
        {
            statusMessage = $"tankPrefab '{tankPrefab.name}' must include NetworkObject + TankPlayerController.";
            Debug.LogError(statusMessage);
            return false;
        }

        tankPrefab = resolvedPrefab;

        NetworkManager manager = EnsureNetworkManager();

        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = manager.gameObject.AddComponent<UnityTransport>();
        }

        NetworkConfig config = manager.NetworkConfig;
        if (config == null)
        {
            config = new NetworkConfig();
            manager.NetworkConfig = config;
        }

        if (config.Prefabs == null)
        {
            config.Prefabs = new NetworkPrefabs();
        }

        config.NetworkTransport = transport;
        config.PlayerPrefab = tankPrefab;

        if (!config.Prefabs.Contains(tankPrefab))
        {
            config.Prefabs.Add(new NetworkPrefab { Prefab = tankPrefab });
        }

        return true;
    }

    static bool HasRequiredNetworkComponents(GameObject prefab)
    {
        return prefab != null &&
               prefab.GetComponent<NetworkObject>() != null &&
               prefab.GetComponent<TankPlayerController>() != null;
    }

    GameObject ResolveNetworkReadyPrefab(GameObject sourcePrefab)
    {
        if (HasRequiredNetworkComponents(sourcePrefab))
        {
            return sourcePrefab;
        }

#if UNITY_EDITOR
        // 1) Try known tank variant path first.
        const string knownTankVariantPath = "Assets/Mesh/tank/tank.prefab";
        GameObject knownVariant = AssetDatabase.LoadAssetAtPath<GameObject>(knownTankVariantPath);
        if (HasRequiredNetworkComponents(knownVariant))
        {
            return knownVariant;
        }

        // 2) Try to auto-fix the currently assigned prefab asset if it is editable.
        string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
        if (!string.IsNullOrEmpty(sourcePath))
        {
            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(sourcePrefab);
            if (assetType != PrefabAssetType.Model)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(sourcePath);
                bool changed = false;

                if (root.GetComponent<NetworkObject>() == null)
                {
                    root.AddComponent<NetworkObject>();
                    changed = true;
                }

                if (root.GetComponent<TankPlayerController>() == null)
                {
                    root.AddComponent<TankPlayerController>();
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, sourcePath);
                }

                PrefabUtility.UnloadPrefabContents(root);

                GameObject reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                if (HasRequiredNetworkComponents(reloaded))
                {
                    return reloaded;
                }
            }
            else
            {
                // Model prefabs cannot be modified directly. Create a network-ready prefab variant.
                const string generatedFolder = "Assets/Generated";
                const string generatedPrefabPath = "Assets/Generated/NetworkTank.prefab";

                if (!AssetDatabase.IsValidFolder(generatedFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "Generated");
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
                if (instance != null)
                {
                    if (instance.GetComponent<NetworkObject>() == null)
                    {
                        instance.AddComponent<NetworkObject>();
                    }

                    if (instance.GetComponent<TankPlayerController>() == null)
                    {
                        instance.AddComponent<TankPlayerController>();
                    }

                    GameObject generatedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, generatedPrefabPath);
                    DestroyImmediate(instance);
                    AssetDatabase.SaveAssets();

                    if (HasRequiredNetworkComponents(generatedPrefab))
                    {
                        return generatedPrefab;
                    }
                }
            }
        }
#endif

        return null;
    }

    void SubscribeNetworkEvents()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    void UnsubscribeNetworkEvents()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    static async Task EnsureSignedInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    void OnGUI()
    {
        if (!showDebugUi)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(16f, 16f, 420f, 680f), GUI.skin.box);
        GUILayout.Label("Tank Multiplayer");
        GUILayout.Label(statusMessage);
        GUILayout.Space(8f);

        GUI.enabled = !isBusy;
        if (currentSession == null)
        {
            if (GUILayout.Button("Host Session"))
            {
                HostFromUi();
            }

            GUILayout.Space(6f);
            GUILayout.Label("Join Code");
            joinCodeInput = GUILayout.TextField(joinCodeInput, 16).Trim().ToUpperInvariant();
            if (GUILayout.Button("Join Session"))
            {
                JoinFromUi();
            }
        }
        else
        {
            GUILayout.Label($"Session: {currentSession.Name}");
            GUILayout.Label($"Code: {currentSession.Code}");
            if (GUILayout.Button("Run My Script"))
            {
                SendRoutineFromUi();
            }

            if (GUILayout.Button("Leave Session"))
            {
                LeaveFromUi();
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("Script Window");
        autoCompileScript = GUILayout.Toggle(autoCompileScript, "Auto-compile while typing");
        loopScriptRoutine = GUILayout.Toggle(loopScriptRoutine, "Loop full routine");

        string updatedScript = GUILayout.TextArea(routineScript, GUILayout.Height(190f));
        if (!string.Equals(updatedScript, routineScript))
        {
            routineScript = updatedScript;
            scriptPreviewDirty = true;
            if (autoCompileScript)
            {
                int maxInstructions = TryGetLocalTank(out TankPlayerController localTank) ? localTank.maxInstructionsPerRoutine : 128;
                CompileScriptPreview(maxInstructions);
            }
        }

        if (GUILayout.Button("Compile Preview"))
        {
            int maxInstructions = TryGetLocalTank(out TankPlayerController localTank) ? localTank.maxInstructionsPerRoutine : 128;
            CompileScriptPreview(maxInstructions);
        }

        if (scriptPreviewDirty)
        {
            GUILayout.Label("Preview is out of date.");
        }

        GUILayout.Label(scriptCompileMessage);
        scriptPreviewScroll = GUILayout.BeginScrollView(scriptPreviewScroll, GUILayout.Height(180f));
        GUILayout.Label(scriptPreviewText);
        GUILayout.EndScrollView();

        GUI.enabled = true;
        GUILayout.EndArea();
    }
}
