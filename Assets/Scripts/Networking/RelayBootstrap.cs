using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Hosts via Unity Relay (returns a join code) or joins via a code.
/// Max 8 players (1 host + 7 clients).
/// </summary>
public class RelayBootstrap : MonoBehaviour
{
    public const int MaxPlayers = 8;
    public const int MaxConnections = MaxPlayers - 1; // host + 7

    public static RelayBootstrap Instance { get; private set; }

    [Tooltip("Scene to load on the host after starting (clients follow via NetworkSceneManager).")]
    public string gameplayScene = "LevelOne";

    [Tooltip("Region. Leave empty for auto.")]
    public string region = "";

    public string LastJoinCode { get; private set; }
    public bool IsBusy { get; private set; }

    public event Action<string> OnStatus;
    public event Action<string> OnError;
    public event Action OnHostStarted;
    public event Action OnClientConnected;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async Task EnsureSignedIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async void StartHost()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            OnStatus?.Invoke("Signing in...");
            await EnsureSignedIn();

            OnStatus?.Invoke("Allocating relay...");
            Allocation alloc = await RelayService.Instance.CreateAllocationAsync(MaxConnections, string.IsNullOrEmpty(region) ? null : region);

            LastJoinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            OnStatus?.Invoke($"Join code: {LastJoinCode}");

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var data = AllocationUtils.ToRelayServerData(alloc, "dtls");
            transport.SetRelayServerData(data);

            if (!NetworkManager.Singleton.StartHost())
            {
                OnError?.Invoke("StartHost failed.");
                return;
            }

            OnHostStarted?.Invoke();

            if (!string.IsNullOrEmpty(gameplayScene))
            {
                var status = NetworkManager.Singleton.SceneManager.LoadScene(gameplayScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                    Debug.LogWarning($"[RelayBootstrap] LoadScene status: {status}");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            OnError?.Invoke(e.Message);
        }
        finally { IsBusy = false; }
    }

    public async void JoinWithCode(string code)
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(code)) { OnError?.Invoke("Empty join code."); return; }

        IsBusy = true;
        try
        {
            OnStatus?.Invoke("Signing in...");
            await EnsureSignedIn();

            OnStatus?.Invoke("Joining relay...");
            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(code.Trim());

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            var data = AllocationUtils.ToRelayServerData(joinAlloc, "dtls");
            transport.SetRelayServerData(data);

            if (!NetworkManager.Singleton.StartClient())
            {
                OnError?.Invoke("StartClient failed.");
                return;
            }

            OnStatus?.Invoke("Connecting...");
            OnClientConnected?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            OnError?.Invoke(e.Message);
        }
        finally { IsBusy = false; }
    }

    public void Disconnect()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        LastJoinCode = null;
    }
}
