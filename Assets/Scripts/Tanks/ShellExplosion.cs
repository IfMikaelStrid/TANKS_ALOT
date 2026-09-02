using System.Collections.Generic;
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

    [Tooltip("Seconds before a shell that never hits anything is removed.")]
    public float maxLifetime = 10f;

    readonly HashSet<TankHealth> damaged = new HashSet<TankHealth>();
    bool exploded;
    float age;

    public override void OnNetworkSpawn()
    {
        if (IsServer) return;

        // Only the server simulates the shell; other peers follow the replicated transform.
        var body = GetComponent<Rigidbody>();
        if (body != null)
            body.isKinematic = true;
    }

    void Update()
    {
        if (!IsAuthority() || exploded) return;

        age += Time.deltaTime;
        if (age < maxLifetime) return;

        exploded = true;
        Remove();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!IsAuthority() || exploded) return;
        exploded = true;

        Vector3 explosionPos = transform.position;
        ApplyDamage(explosionPos);

        if (IsSpawned)
            ExplodeRpc(explosionPos);
        else
            SpawnEffects(explosionPos);

        Remove();
    }

    void ApplyDamage(Vector3 explosionPos)
    {
        damaged.Clear();

        Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
        foreach (Collider hit in hits)
        {
            TankHealth health = hit.GetComponentInParent<TankHealth>();

            // A tank has several colliders; without this it takes damage once per collider.
            if (health != null && damaged.Add(health))
                health.TakeDamage(damage);

            Rigidbody rb = hit.GetComponent<Rigidbody>();
            if (rb != null)
                rb.AddExplosionForce(explosionForce, explosionPos, explosionRadius);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void ExplodeRpc(Vector3 position)
    {
        SpawnEffects(position);
    }

    void SpawnEffects(Vector3 position)
    {
        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, position, Quaternion.identity);
            Destroy(effect, effectDuration);
        }

        if (smokeRingEffectPrefab != null)
        {
            GameObject smokeEffect = Instantiate(smokeRingEffectPrefab, position, Quaternion.identity);
            Destroy(smokeEffect, effectDuration * 1.5f);
        }
    }

    void Remove()
    {
        if (IsSpawned)
            NetworkObject.Despawn(true);
        else
            Destroy(gameObject);
    }

    bool IsAuthority() => !IsSpawned || IsServer;
}
