using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-built host/join menu. Pair with a RelayBootstrap + NetworkManager in scene.
/// </summary>
public class MultiplayerMenuUI : MonoBehaviour
{
    static readonly Color PanelBg = new Color(0.10f, 0.10f, 0.12f, 0.94f);
    static readonly Color BtnBg   = new Color(0.24f, 0.24f, 0.28f, 1f);
    static readonly Color HostBg  = new Color(0.20f, 0.55f, 0.20f, 1f);
    static readonly Color JoinBg  = new Color(0.20f, 0.35f, 0.60f, 1f);
    static readonly Color Txt     = new Color(0.90f, 0.92f, 0.90f, 1f);
    static readonly Color Dim     = new Color(0.60f, 0.65f, 0.60f, 1f);

    Canvas canvas;
    Text statusText;
    Text codeText;
    InputField codeInput;

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
    }

    void OnEnable()
    {
        if (RelayBootstrap.Instance != null)
        {
            RelayBootstrap.Instance.OnStatus += SetStatus;
            RelayBootstrap.Instance.OnError += SetError;
            RelayBootstrap.Instance.OnHostStarted += HandleHostStarted;
        }
    }

    void OnDisable()
    {
        if (RelayBootstrap.Instance != null)
        {
            RelayBootstrap.Instance.OnStatus -= SetStatus;
            RelayBootstrap.Instance.OnError -= SetError;
            RelayBootstrap.Instance.OnHostStarted -= HandleHostStarted;
        }
    }

    void HandleHostStarted()
    {
        var code = RelayBootstrap.Instance != null ? RelayBootstrap.Instance.LastJoinCode : "";
        codeText.text = string.IsNullOrEmpty(code) ? "" : $"Join code: {code}";
    }

    void SetStatus(string s) { if (statusText != null) { statusText.color = Dim; statusText.text = s; } }
    void SetError(string s)  { if (statusText != null) { statusText.color = new Color(0.95f,0.4f,0.35f,1f); statusText.text = s; } }

    void OnHostClicked()
    {
        if (RelayBootstrap.Instance == null) { SetError("No RelayBootstrap in scene."); return; }
        RelayBootstrap.Instance.StartHost();
    }

    void OnJoinClicked()
    {
        if (RelayBootstrap.Instance == null) { SetError("No RelayBootstrap in scene."); return; }
        RelayBootstrap.Instance.JoinWithCode(codeInput != null ? codeInput.text : "");
    }

    void OnCopyClicked()
    {
        if (RelayBootstrap.Instance != null && !string.IsNullOrEmpty(RelayBootstrap.Instance.LastJoinCode))
            GUIUtility.systemCopyBuffer = RelayBootstrap.Instance.LastJoinCode;
    }

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("MultiplayerMenuCanvas");
        canvasGo.transform.SetParent(transform);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(420, 320);
        panel.anchoredPosition = Vector2.zero;
        AddImage(panel, PanelBg);

        var title = NewText("Title", panel, "TANKS — Multiplayer", 24, Txt, TextAnchor.MiddleCenter);
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1); titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1); titleRect.anchoredPosition = new Vector2(0, -10);
        titleRect.sizeDelta = new Vector2(0, 40);

        var hostBtn = AddButton(panel, "Host Game", new Vector2(0, -70), new Vector2(380, 50), HostBg, Txt, OnHostClicked);
        codeText = NewText("Code", panel, "", 18, Txt, TextAnchor.MiddleCenter);
        var codeRect = codeText.rectTransform;
        codeRect.anchorMin = new Vector2(0, 1); codeRect.anchorMax = new Vector2(1, 1);
        codeRect.pivot = new Vector2(0.5f, 1); codeRect.anchoredPosition = new Vector2(0, -130);
        codeRect.sizeDelta = new Vector2(0, 30);

        AddButton(panel, "Copy Code", new Vector2(0, -165), new Vector2(180, 28), BtnBg, Txt, OnCopyClicked);

        codeInput = AddInput(panel, "Enter join code", new Vector2(-95, -215), new Vector2(180, 40));
        AddButton(panel, "Join", new Vector2(110, -215), new Vector2(160, 40), JoinBg, Txt, OnJoinClicked);

        statusText = NewText("Status", panel, "", 14, Dim, TextAnchor.MiddleCenter);
        var sRect = statusText.rectTransform;
        sRect.anchorMin = new Vector2(0, 1); sRect.anchorMax = new Vector2(1, 1);
        sRect.pivot = new Vector2(0.5f, 1); sRect.anchoredPosition = new Vector2(0, -275);
        sRect.sizeDelta = new Vector2(0, 30);
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    static Image AddImage(RectTransform r, Color c) { var img = r.gameObject.AddComponent<Image>(); img.color = c; return img; }

    static Text NewText(string name, RectTransform parent, string content, int size, Color color, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content; t.fontSize = size; t.color = color; t.alignment = align;
        return t;
    }

    static Button AddButton(RectTransform parent, string label, Vector2 pos, Vector2 size, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
    {
        var rect = NewRect(label, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        var img = rect.gameObject.AddComponent<Image>(); img.color = bg;
        var btn = rect.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        var txt = NewText("Label", rect, label, 18, fg, TextAnchor.MiddleCenter);
        return btn;
    }

    static InputField AddInput(RectTransform parent, string placeholder, Vector2 pos, Vector2 size)
    {
        var rect = NewRect("Input", parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        var img = rect.gameObject.AddComponent<Image>(); img.color = new Color(0.07f, 0.07f, 0.09f, 1f);
        var input = rect.gameObject.AddComponent<InputField>();
        input.characterLimit = 8;
        var textComp = NewText("Text", rect, "", 18, new Color(0.9f,0.92f,0.9f,1f), TextAnchor.MiddleLeft);
        var tRect = textComp.rectTransform; tRect.offsetMin = new Vector2(8,0); tRect.offsetMax = new Vector2(-8,0);
        var ph = NewText("Placeholder", rect, placeholder, 16, new Color(0.55f,0.6f,0.55f,1f), TextAnchor.MiddleLeft);
        var pRect = ph.rectTransform; pRect.offsetMin = new Vector2(8,0); pRect.offsetMax = new Vector2(-8,0);
        ph.fontStyle = FontStyle.Italic;
        input.textComponent = textComp; input.placeholder = ph;
        return input;
    }
}
