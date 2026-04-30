using Unity.Netcode;
using UnityEngine;

public class TankFiring : NetworkBehaviour
{
    [Header("Firing")]
    public GameObject bulletPrefab;
    public float launchForce = 20f;

    [Header("Cooldown")]
    public float shootCooldown = 2f;

    Transform fireTransform;
    float _lastFireTime = float.MinValue;

    void Awake() { fireTransform = transform.Find("FireTransform"); }

    void OnEnable()  { TankEventBus.OnFire += HandleFire; }
    void OnDisable() { TankEventBus.OnFire -= HandleFire; }

    void HandleFire(int playerNumber)
    {
        var listener = GetComponentInParent<InputListener>();
        if (listener == null || playerNumber != listener.PlayerNumber) return;

        // When networked, only the server fires.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !IsServer)
            return;

        if (Time.time - _lastFireTime < shootCooldown)
        {
            TankEventBus.CommandDone(playerNumber);
            return;
        }

        if (bulletPrefab == null || fireTransform == null)
        {
            Debug.LogWarning($"[TankFiring] {gameObject.name} missing bulletPrefab or fireTransform.");
            TankEventBus.CommandDone(playerNumber);
            return;
        }

        GameObject bullet = Instantiate(bulletPrefab, fireTransform.position, fireTransform.rotation);

        var rb = bullet.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = fireTransform.forward * launchForce;

        // Spawn over the network if a NetworkObject is present and we're networked.
        var no = bullet.GetComponent<NetworkObject>();
        if (no != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsServer)
            no.Spawn(true);

        _lastFireTime = Time.time;
        TankEventBus.CommandDone(playerNumber);
    }
}
