using System.Collections;
using Unity.Netcode.Components;
using UnityEngine;

public class TankUprightCorrector : MonoBehaviour
{
    [Tooltip("Dot product threshold below which the tank is considered tipped (0.5 ≈ 60°).")]
    public float tiltThreshold = 0.5f;

    [Tooltip("If still tipped after this many seconds, reset upright at the tank's current position.")]
    public float hardResetTimeout = 5f;

    Quaternion originalRotation;
    Rigidbody body;
    float tiltTimer;
    bool movementPaused;
    bool resetting;

    void Awake()
    {
        originalRotation = transform.rotation;
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // Clients must not touch the transform or they fight the replicated rotation.
        if (!TankNetworkContext.SimulatesTanks) return;

        float upDot = Vector3.Dot(transform.up, Vector3.up);

        if (upDot >= tiltThreshold)
        {
            tiltTimer = 0f;
            if (movementPaused)
                FinishPausedCommand();
            return;
        }

        if (!movementPaused)
            PauseMovement();

        tiltTimer += Time.fixedDeltaTime;

        if (tiltTimer >= hardResetTimeout && !resetting)
            StartCoroutine(ResetToSpawnRoutine());
    }

    IEnumerator ResetToSpawnRoutine()
    {
        resetting = true;
        tiltTimer = 0f;
        Vector3 resetPosition = transform.position;

        bool wasKinematic = false;

        if (body != null)
        {
            wasKinematic = body.isKinematic;
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        var netTransform = GetComponent<NetworkTransform>();
        if (netTransform != null && netTransform.IsSpawned)
        {
            netTransform.Teleport(resetPosition, originalRotation, transform.localScale);
        }
        else
        {
            transform.SetPositionAndRotation(resetPosition, originalRotation);
        }

        if (body != null)
        {
            yield return new WaitForFixedUpdate();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = wasKinematic;
        }

        FinishPausedCommand();
        resetting = false;
    }

    void PauseMovement()
    {
        var listener = GetComponent<InputListener>();
        if (listener == null) return;

        listener.StopAllCoroutines();
        movementPaused = true;
    }

    void FinishPausedCommand()
    {
        if (!movementPaused) return;

        var listener = GetComponent<InputListener>();
        if (listener == null) return;

        movementPaused = false;
        TankEventBus.CommandDone(listener.playerNumber);
    }
}
