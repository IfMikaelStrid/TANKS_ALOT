using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Top-down pan/zoom camera. Left-drag pans across the ground plane, WASD pans
/// independently of the mouse, scroll zooms along the camera's own forward axis,
/// Space snaps back to the local player's tank. Orientation never changes.
/// Keyboard controls are suppressed while a UI text field (e.g. the console
/// script editor) has focus, so typing doesn't drive the camera.
/// </summary>
public class FreeCamera : MonoBehaviour
{
    [Header("Pan")]
    [Tooltip("World units moved per pixel of mouse drag.")]
    public float dragPanSpeed = 0.05f;
    [Tooltip("World units per second when panning with WASD.")]
    public float keyPanSpeed = 20f;

    [Header("Zoom")]
    [Tooltip("World units moved per scroll unit.")]
    public float zoomSpeed = 0.02f;
    public float minHeight = 5f;
    public float maxHeight = 80f;

    [Header("Reset")]
    [Tooltip("Distance from the tank along the camera's current view direction when Space is pressed.")]
    public float resetDistance = 40f;

    void Update()
    {
        bool typing = IsTypingInUI();

        HandlePan(typing);
        HandleZoom();
        HandleReset(typing);
    }

    void HandlePan(bool typing)
    {
        Vector3 forward = GroundForward();

        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();

            // Dragging the mouse moves the world under the cursor, so pan is inverted.
            transform.position += -transform.right * delta.x * dragPanSpeed - forward * delta.y * dragPanSpeed;
        }

        if (typing) return;

        if (Keyboard.current.wKey.isPressed) transform.position += forward * keyPanSpeed * Time.deltaTime;
        if (Keyboard.current.sKey.isPressed) transform.position -= forward * keyPanSpeed * Time.deltaTime;
        if (Keyboard.current.aKey.isPressed) transform.position -= transform.right * keyPanSpeed * Time.deltaTime;
        if (Keyboard.current.dKey.isPressed) transform.position += transform.right * keyPanSpeed * Time.deltaTime;
    }

    void HandleZoom()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        Vector3 next = transform.position + transform.forward * scroll * zoomSpeed;
        next.y = Mathf.Clamp(next.y, minHeight, maxHeight);
        transform.position = next;
    }

    void HandleReset(bool typing)
    {
        if (typing) return;
        if (!Keyboard.current.spaceKey.wasPressedThisFrame) return;

        Transform tank = FindLocalTank();
        if (tank == null) return;

        // Placing the camera along its own forward axis keeps the tank centered
        // regardless of tilt, unlike a fixed world-space offset.
        transform.position = tank.position - transform.forward * resetDistance;
    }

    /// <summary>True while a uGUI InputField (the console script editor) has keyboard focus.</summary>
    static bool IsTypingInUI()
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        var selected = eventSystem.currentSelectedGameObject;
        if (selected == null) return false;

        var inputField = selected.GetComponent<InputField>();
        return inputField != null && inputField.isFocused;
    }

    Vector3 GroundForward()
    {
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.up;
    }

    static Transform FindLocalTank()
    {
        if (TankNetworkContext.SessionActive)
        {
            foreach (var identity in FindObjectsByType<TankNetworkIdentity>(FindObjectsSortMode.None))
            {
                if (identity.IsLocalPlayerTank)
                    return identity.transform;
            }
            return null;
        }

        var listener = FindFirstObjectByType<InputListener>();
        return listener != null ? listener.transform : null;
    }
}