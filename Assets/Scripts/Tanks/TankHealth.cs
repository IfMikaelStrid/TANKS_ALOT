using Unity.Netcode;
using UnityEngine;

public class TankHealth : NetworkBehaviour
{
    public int maxHealth = 3;

    readonly NetworkVariable<int> m_NetHealth = new NetworkVariable<int>();

    private int currentHealth;

    public int CurrentHealth => IsSpawned ? m_NetHealth.Value : currentHealth;
    public bool IsAlive => CurrentHealth > 0;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            m_NetHealth.Value = currentHealth;
    }

    public void TakeDamage(int amount)
    {
        if (!CanMutate() || amount <= 0) return;
        if (currentHealth <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        Publish();

        Debug.Log($"[TankHealth] {gameObject.name} took {amount} damage. Health: {currentHealth}/{maxHealth}");

        if (currentHealth > 0) return;

        Debug.Log($"[TankHealth] {gameObject.name} destroyed!");

        var listener = GetComponent<InputListener>();
        if (listener != null)
            TankEventBus.TankDestroyed(listener.playerNumber);

        SetVisiblyAlive(false);
    }

    public void ResetHealth()
    {
        if (!CanMutate()) return;

        currentHealth = maxHealth;
        Publish();
        SetVisiblyAlive(true);
    }

    /// <summary>Damage is resolved by the authority only.</summary>
    bool CanMutate() => !IsSpawned || IsServer;

    void Publish()
    {
        if (IsSpawned && IsServer)
            m_NetHealth.Value = currentHealth;
    }

    void SetVisiblyAlive(bool alive)
    {
        var identity = GetComponent<TankNetworkIdentity>();
        if (identity != null && identity.IsSpawned)
        {
            identity.SetAlive(alive);
            return;
        }

        gameObject.SetActive(alive);
    }
}
