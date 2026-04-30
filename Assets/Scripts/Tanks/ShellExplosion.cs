using Unity.Netcode;
using UnityEngine;

public class ShellExplosion : NetworkBehaviour
{
    public GameObject explosionEffectPrefab;
    public GameObject smokeRingEffectPrefab;
    public float effectDuration = 2f;
    public float explosionForce = 500f;
    public float explosionRadius = 5f;
    public int damage = 1;

    bool _exploded;

    void OnCollisionEnter(Collision collision)
    {
        // Damage logic must run on the server only when networked.
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked && !IsServer)
        {
            if (!_exploded) PlayEffectsLocal();
            return;
        }

        if (_exploded) return;
        _exploded = true;

        Vector3 explosionPos = transform.position;
        Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
        foreach (Collider hit in hits)
        {
            var health = hit.GetComponentInParent<TankHealth>();
            if (health != null)
            {
                Debug.Log($"TAKING DAMAGE: {hit.gameObject.name} (Health: {health.CurrentHealth})");
                health.TakeDamage(damage);
            }

            var rb = hit.GetComponent<Rigidbody>();
            if (rb != null) rb.AddExplosionForce(explosionForce, explosionPos, explosionRadius);
        }

        PlayEffectsLocal();
        PlayEffectsClientRpc(explosionPos);

        if (networked && IsSpawned) NetworkObject.Despawn(true);
        else Destroy(gameObject);
    }

    [Rpc(SendTo.NotServer)]
    void PlayEffectsClientRpc(Vector3 pos)
    {
        SpawnEffect(explosionEffectPrefab, pos, effectDuration);
        SpawnEffect(smokeRingEffectPrefab, pos, effectDuration * 1.5f);
    }

    void PlayEffectsLocal()
    {
        SpawnEffect(explosionEffectPrefab, transform.position, effectDuration);
        SpawnEffect(smokeRingEffectPrefab, transform.position, effectDuration * 1.5f);
    }

    void SpawnEffect(GameObject prefab, Vector3 pos, float duration)
    {
        if (prefab == null) return;
        var effect = Instantiate(prefab, pos, Quaternion.identity);
        Destroy(effect, duration);
    }
}
