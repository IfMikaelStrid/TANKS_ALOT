using Unity.Netcode;
using UnityEngine;

public class TankHealth : NetworkBehaviour
{
    public int maxHealth = 3;

    readonly NetworkVariable<int> _health = new NetworkVariable<int>(3, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    int _offlineHealth;

    public int CurrentHealth => IsSpawned ? _health.Value : _offlineHealth;
    public bool IsAlive => CurrentHealth > 0;

    void Awake() { _offlineHealth = maxHealth; }

    public override void OnNetworkSpawn()
    {
        if (IsServer) _health.Value = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (IsSpawned)
        {
            if (!IsServer) return;
            if (_health.Value <= 0) return;

            int newHp = Mathf.Max(0, _health.Value - amount);
            _health.Value = newHp;
            Debug.Log($"[TankHealth] {gameObject.name} took {amount} damage. Health: {newHp}/{maxHealth}");

            if (newHp <= 0)
            {
                Debug.Log($"[TankHealth] {gameObject.name} destroyed!");
                NotifyDestroyed();
                BroadcastDestroyedClientRpc();
                gameObject.SetActive(false);
            }
        }
        else
        {
            // Offline mode
            if (_offlineHealth <= 0) return;
            _offlineHealth = Mathf.Max(0, _offlineHealth - amount);
            Debug.Log($"[TankHealth] {gameObject.name} took {amount} damage. Health: {_offlineHealth}/{maxHealth}");
            if (_offlineHealth <= 0)
            {
                NotifyDestroyed();
                gameObject.SetActive(false);
            }
        }
    }

    void NotifyDestroyed()
    {
        var listener = GetComponent<InputListener>();
        if (listener != null) TankEventBus.TankDestroyed(listener.PlayerNumber);
    }

    [Rpc(SendTo.NotServer)]
    void BroadcastDestroyedClientRpc()
    {
        var listener = GetComponent<InputListener>();
        if (listener != null) TankEventBus.TankDestroyed(listener.PlayerNumber);
        gameObject.SetActive(false);
    }

    public void ResetHealth()
    {
        if (IsSpawned)
        {
            if (!IsServer) return;
            _health.Value = maxHealth;
            ReactivateClientRpc();
            gameObject.SetActive(true);
        }
        else
        {
            _offlineHealth = maxHealth;
            gameObject.SetActive(true);
        }
    }

    [Rpc(SendTo.NotServer)]
    void ReactivateClientRpc() { gameObject.SetActive(true); }
}
