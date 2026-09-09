using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows one small tank icon per remaining health point in a world-space canvas
/// above the tank. Polls TankHealth so it works identically offline and networked.
/// </summary>
[DisallowMultipleComponent]
public class TankHealthBarUI : MonoBehaviour
{
    [Tooltip("Icon shown once per remaining health point.")]
    public Sprite healthIconSprite;

    [Header("Layout")]
    public float heightOffset = 2.2f;
    public float forwardOffset = 2.5f;
    public float iconSizePixels = 64f;
    public float iconSpacingPixels = 76f;
    public float canvasScale = 0.01f;

    TankHealth health;
    Canvas canvas;
    RectTransform canvasRect;
    readonly List<Image> icons = new List<Image>();
    int builtForMaxHealth = -1;
    Camera cam;

    void Awake()
    {
        health = GetComponent<TankHealth>();
        BuildCanvas();
    }

    void LateUpdate()
    {
        if (health == null) return;

        if (builtForMaxHealth != health.maxHealth)
            BuildIcons(health.maxHealth);

        int hp = Mathf.Clamp(health.CurrentHealth, 0, icons.Count);
        for (int i = 0; i < icons.Count; i++)
            icons[i].gameObject.SetActive(i < hp);

        // World-space offset so the icons don't swing around as the tank turns.
        canvasRect.position = transform.position + Vector3.up * heightOffset + Vector3.forward * forwardOffset;

        Billboard();
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("HealthCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localScale = Vector3.one * canvasScale;

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(iconSpacingPixels, iconSizePixels);
    }

    void BuildIcons(int maxHealth)
    {
        foreach (var icon in icons)
        {
            if (icon != null) Destroy(icon.gameObject);
        }
        icons.Clear();

        maxHealth = Mathf.Max(0, maxHealth);
        builtForMaxHealth = maxHealth;

        float totalWidth = Mathf.Max(0, maxHealth - 1) * iconSpacingPixels;
        canvasRect.sizeDelta = new Vector2(totalWidth + iconSizePixels, iconSizePixels);

        for (int i = 0; i < maxHealth; i++)
        {
            var iconGo = new GameObject($"Icon_{i}");
            iconGo.transform.SetParent(canvasRect, false);

            var image = iconGo.AddComponent<Image>();
            image.sprite = healthIconSprite;
            image.preserveAspect = true;

            var rect = image.rectTransform;
            rect.sizeDelta = new Vector2(iconSizePixels, iconSizePixels);
            rect.anchoredPosition = new Vector2(-totalWidth * 0.5f + i * iconSpacingPixels, 0f);

            icons.Add(image);
        }
    }

    void Billboard()
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        canvasRect.rotation = cam.transform.rotation;
    }
}
