using System.Collections;
using UnityEngine;

public class InputListener : MonoBehaviour
{
    [Header("Player")]
    public int playerNumber = 1;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;
    public float boostDistance = 8f;
    public float boostSpeed = 40f;

    void OnEnable()
    {
        TankEventBus.OnMoveForward += HandleMoveForward;
        TankEventBus.OnTurn += HandleTurn;
        TankEventBus.OnBoost += HandleBoost;
    }

    void OnDisable()
    {
        TankEventBus.OnMoveForward -= HandleMoveForward;
        TankEventBus.OnTurn -= HandleTurn;
        TankEventBus.OnBoost -= HandleBoost;
    }

    private void HandleMoveForward(int playerNumber, float distance)
    {
        if (playerNumber != this.playerNumber) return;
        StartCoroutine(MoveForwardRoutine(distance));
    }

    private void HandleTurn(int playerNumber, float degrees, float arcRadius)
    {
        if (playerNumber != this.playerNumber) return;
        if (arcRadius > 0f)
            StartCoroutine(ArcTurnRoutine(degrees, arcRadius));
        else
            StartCoroutine(TurnRoutine(degrees));
    }

    private IEnumerator MoveForwardRoutine(float distance)
    {
        float moved = 0f;
        float direction = Mathf.Sign(distance);
        float total = Mathf.Abs(distance);

        while (moved < total)
        {
            float step = moveSpeed * Time.deltaTime;
            if (moved + step > total) step = total - moved;
            transform.position += transform.forward * step * direction;
            moved += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} moved forward {distance} units.");
        TankEventBus.CommandDone(playerNumber);
    }

    private IEnumerator TurnRoutine(float degrees)
    {
        float turned = 0f;
        float direction = Mathf.Sign(degrees);
        float total = Mathf.Abs(degrees);

        while (turned < total)
        {
            float step = rotateSpeed * Time.deltaTime;
            if (turned + step > total) step = total - turned;
            transform.Rotate(0f, step * direction, 0f);
            turned += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} turned {degrees} degrees.");
        TankEventBus.CommandDone(playerNumber);
    }

    private IEnumerator ArcTurnRoutine(float degrees, float radius)
    {
        float turned = 0f;
        float direction = Mathf.Sign(degrees);
        float total = Mathf.Abs(degrees);

        // Pivot point is offset to the right (positive) or left (negative) of the tank
        Vector3 pivotOffset = transform.right * radius * direction;

        while (turned < total)
        {
            float step = rotateSpeed * Time.deltaTime;
            if (turned + step > total) step = total - turned;

            Vector3 pivot = transform.position + pivotOffset;
            transform.RotateAround(pivot, Vector3.up, step * direction);

            // Recalculate pivot offset after rotation
            pivotOffset = transform.right * radius * direction;

            turned += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} arc-turned {degrees} degrees with radius {radius}.");
        TankEventBus.CommandDone(playerNumber);
    }

    private void HandleBoost(int playerNumber)
    {
        if (playerNumber != this.playerNumber) return;
        StartCoroutine(BoostRoutine());
    }

    private IEnumerator BoostRoutine()
    {
        float moved = 0f;

        while (moved < boostDistance)
        {
            float t = moved / boostDistance;
            // Ease-out: starts fast, decelerates
            float speed = Mathf.Lerp(boostSpeed, moveSpeed, t * t);
            float step = speed * Time.deltaTime;
            if (moved + step > boostDistance) step = boostDistance - moved;
            transform.position += transform.forward * step;
            moved += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} boosted {boostDistance} units.");
        TankEventBus.CommandDone(playerNumber);
    }
}
