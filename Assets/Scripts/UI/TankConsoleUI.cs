using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// In-game collapsible console window for authoring and running TankScript.
/// Attach to any GameObject. The entire UI is built at runtime.
/// Press ` (backtick) to toggle, or click the header tab.
/// </summary>
public class TankConsoleUI : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Must match the InputListener.playerNumber of the tank you own.")]
    public int playerNumber = 1;

    [Header("Behaviour")]
    public Key toggleKey = Key.Backquote;
    public int maxConsoleLines = 200;
    public float delayBetweenCommands = 0.1f;

    InputListener _ownerTank;

    [Header("Default Script")]
    [TextArea(3, 8)]
    public string defaultScript = "FOR 4\nMOVE 5\nTURN 90\nEND";

    // ── runtime state ──
    bool collapsed = true;
    bool looping;
    bool running;
    readonly List<string> logLines = new List<string>();

    // ── game mode state ──
    bool hasSubmittedOnce;
    bool waitingForReactiveInput;
    bool roundOver;

    // ── ui handles ──
    Canvas canvas;
    RectTransform panelRect;
    RectTransform headerRect;
    RectTransform bodyRect;
    InputField scriptInput;
    Text consoleText;
    ScrollRect scrollRect;
    Button playBtn;
    Button stopBtn;
    Toggle loopToggle;
    Text headerLabel;
    InputAction toggleAction;

    // ── style ──
    static readonly Color PanelBg      = new Color(0.10f, 0.10f, 0.12f, 0.94f);
    static readonly Color HeaderBg     = new Color(0.16f, 0.16f, 0.20f, 1f);
    static readonly Color InputBg      = new Color(0.07f, 0.07f, 0.09f, 1f);
    static readonly Color ConsoleBg    = new Color(0.06f, 0.06f, 0.08f, 1f);
    static readonly Color BtnNormal    = new Color(0.24f, 0.24f, 0.28f, 1f);
    static readonly Color PlayGreen    = new Color(0.20f, 0.55f, 0.20f, 1f);
    static readonly Color StopRed      = new Color(0.60f, 0.20f, 0.20f, 1f);
    static readonly Color LoopBlue     = new Color(0.20f, 0.35f, 0.60f, 1f);
    static readonly Color Txt          = new Color(0.85f, 0.90f, 0.85f, 1f);
    static readonly Color TxtDim       = new Color(0.55f, 0.60f, 0.55f, 1f);
    static readonly Color LogGreen     = new Color(0.50f, 0.85f, 0.50f, 1f);
    static readonly Color LogError     = new Color(0.95f, 0.40f, 0.35f, 1f);
    static readonly Color LogWarning   = new Color(0.95f, 0.80f, 0.30f, 1f);

    const float PanelWidth       = 460f;
    const float HeaderHeight     = 30f;
    const float BodyHeight       = 440f;
    const float ScriptAreaHeight = 150f;
    const float ToolbarHeight    = 34f;
    const float Pad              = 6f;

    // ═══════════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════════

    void Awake()
    {
        EnsureEventSystem();
        BuildUI();
        SetCollapsed(true);
        Log("Tank Console ready. Player " + playerNumber, TxtDim);
        Log("Type a script and press Play, or toggle Loop.", TxtDim);
    }

    void OnEnable()
    {
        TankEventBus.OnRoundStarted += HandleRoundStarted;
        TankEventBus.OnRoundEnded += HandleRoundEnded;
        TankEventBus.OnGameOver += HandleGameOver;
        TankEventBus.OnReactiveInterval += HandleReactiveInterval;
        TankEventBus.OnRoundTimerResumed += HandleTimerResumed;
        TankEventBus.OnScriptError += HandleScriptError;

        toggleAction = new InputAction("ToggleConsole", InputActionType.Button,
            $"<Keyboard>/{toggleKey}");
        toggleAction.performed += _ => ToggleConsole();
        toggleAction.Enable();
    }

    void OnDisable()
    {
        TankEventBus.OnRoundStarted -= HandleRoundStarted;
        TankEventBus.OnRoundEnded -= HandleRoundEnded;
        TankEventBus.OnGameOver -= HandleGameOver;
        TankEventBus.OnReactiveInterval -= HandleReactiveInterval;
        TankEventBus.OnRoundTimerResumed -= HandleTimerResumed;
        TankEventBus.OnScriptError -= HandleScriptError;

        if (toggleAction != null)
        {
            toggleAction.Disable();
            toggleAction.Dispose();
            toggleAction = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════

    public void ToggleConsole()
    {
        SetCollapsed(!collapsed);
    }

    public void Play()
    {
        string src = scriptInput != null ? scriptInput.text : "";
        if (string.IsNullOrWhiteSpace(src)) { Log("Script is empty.", LogWarning); return; }

        // Local parse just for friendly error feedback before sending.
        List<TankNode> nodes;
        try { nodes = TankScriptParser.Parse(src); }
        catch (FormatException e) { Log("Parse error: " + e.Message, LogError); return; }
        if (nodes.Count == 0) { Log("Script produced no commands.", LogWarning); return; }

        var tank = ResolveOwnerTank();
        if (tank == null) { Log("No owned tank yet — wait for spawn.", LogWarning); return; }

        playerNumber = tank.PlayerNumber;
        hasSubmittedOnce = true;
        running = true;
        roundOver = false;
        UpdateButtonStates();
        Log("\u25b6 Submitted to server.", LogGreen);

        if (NetworkActive())
            tank.SubmitScriptServerRpc(src);
        else
            Log("Offline mode: server RPC skipped.", LogWarning);

        if (GetCurrentGameMode() != GameMode.Dev)
            TankEventBus.PlayerSubmitted(playerNumber);
    }

    bool NetworkActive() => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    InputListener ResolveOwnerTank()
    {
        if (_ownerTank != null && _ownerTank.gameObject != null) return _ownerTank;

        if (!NetworkActive())
        {
            // Offline fallback: pick by playerNumber.
            foreach (var l in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
                if (l.playerNumber == playerNumber) { _ownerTank = l; break; }
            return _ownerTank;
        }

        foreach (var l in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
        {
            if (l.IsOwner) { _ownerTank = l; return l; }
        }
        return null;
    }

    void PlayActive(List<TankNode> nodes) { Play(); }
    void PlayPassive(List<TankNode> nodes) { Play(); }
    void PlayReactive(List<TankNode> nodes) { Play(); }
    void PlayDev(List<TankNode> nodes) { Play(); }

    public void Stop()
    {
        StopRunning();
        Log("\u25a0 Stopped.", LogWarning);
    }

    void StopRunning()
    {
        var tank = ResolveOwnerTank();
        if (tank != null && NetworkActive())
            tank.StopScriptServerRpc();

        running = false;
        waitingForReactiveInput = false;
        UpdateButtonStates();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game event handlers
    // ═══════════════════════════════════════════════════════════════

    GameMode GetCurrentGameMode()
    {
        return GameManager.Instance != null ? GameManager.Instance.gameMode : GameMode.Dev;
    }

    void HandleRoundStarted(int roundNumber)
    {
        roundOver = false;
        Log($"═ Round {roundNumber} started! ═", LogGreen);

        // In passive mode, auto-start running the script
        if (GetCurrentGameMode() == GameMode.Passive)
            AutoPlayPassive();
    }

    void AutoPlayPassive()
    {
        string src = scriptInput != null ? scriptInput.text : "";
        if (string.IsNullOrWhiteSpace(src))
            src = defaultScript;

        List<TankNode> nodes;
        try
        {
            nodes = TankScriptParser.Parse(src);
        }
        catch (FormatException e)
        {
            Log("Parse error: " + e.Message, LogError);
            return;
        }

        if (nodes.Count == 0)
        {
            Log("Script produced no commands.", LogWarning);
            return;
        }

        PlayPassive(nodes);
    }

    void HandleRoundEnded(int roundNumber, int winner)
    {
        roundOver = true;
        string winText = winner > 0 ? $"Player {winner} wins!" : "Draw!";
        Log($"═ Round {roundNumber} ended. {winText} ═", LogWarning);

        // stop execution when round ends (Passive/Reactive)
        StopRunning();
    }

    void HandleGameOver(int winner)
    {
        string winText = winner > 0 ? $"Player {winner} wins the game!" : "No winner.";
        Log($"══ GAME OVER. {winText} ══", LogGreen);
        StopRunning();
    }

    void HandleReactiveInterval(int pn)
    {
        if (pn != playerNumber) return;

        waitingForReactiveInput = true;
        Log("⏸ Reactive interval — update your script and press Play.", LogWarning);

        // enable input
        if (scriptInput != null)
            scriptInput.interactable = true;
        if (playBtn != null)
            playBtn.interactable = true;
    }

    void HandleTimerResumed()
    {
        waitingForReactiveInput = false;
    }

    void HandleScriptError(int pn, string message)
    {
        if (pn != playerNumber) return;
        Log("Server: " + message, LogError);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Console log
    // ═══════════════════════════════════════════════════════════════

    void Log(string message, Color color)
    {
        string hex = ColorUtility.ToHtmlStringRGB(color);
        logLines.Add($"<color=#{hex}>{EscapeRichText(message).ToUpper()}</color>");

        while (logLines.Count > maxConsoleLines)
            logLines.RemoveAt(0);

        if (consoleText != null)
        {
            consoleText.text = string.Join("\n", logLines.ToArray());

            // auto-scroll to bottom
            if (scrollRect != null)
                StartCoroutine(ScrollToBottom());
        }
    }

    IEnumerator ScrollToBottom()
    {
        yield return null; // wait one frame for layout
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f;
    }

    static string EscapeRichText(string s)
    {
        return s.Replace("<", "‹").Replace(">", "›");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Collapse / Expand
    // ═══════════════════════════════════════════════════════════════

    void SetCollapsed(bool value)
    {
        collapsed = value;
        if (bodyRect != null) bodyRect.gameObject.SetActive(!collapsed);
        if (headerLabel != null)
            headerLabel.text = collapsed
                ? $"► Tank Console [P{playerNumber}]"
                : $"▼ Tank Console [P{playerNumber}]";
    }

    // ═══════════════════════════════════════════════════════════════
    //  Button states
    // ═══════════════════════════════════════════════════════════════

    void UpdateButtonStates()
    {
        if (playBtn != null) playBtn.interactable = !running;
        if (stopBtn != null) stopBtn.interactable = running;
    }

    // ═══════════════════════════════════════════════════════════════
    //  UI Construction
    // ═══════════════════════════════════════════════════════════════

    static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;

        GameObject esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    void BuildUI()
    {
        // Canvas
        GameObject canvasGo = new GameObject("TankConsoleCanvas");
        canvasGo.transform.SetParent(transform);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Root panel – anchored bottom-left
        panelRect = CreateRect("ConsolePanel", canvasGo.transform,
            new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(Pad, Pad),
            new Vector2(PanelWidth + Pad, HeaderHeight + BodyHeight + Pad));

        // Header bar (always visible)
        headerRect = CreateRect("Header", panelRect,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -HeaderHeight),
            new Vector2(0, 0));
        AddImage(headerRect, HeaderBg);

        // Header button (fills header)
        GameObject headerBtnGo = headerRect.gameObject;
        Button headerBtn = headerBtnGo.AddComponent<Button>();
        headerBtn.transition = Selectable.Transition.None;
        headerBtn.onClick.AddListener(ToggleConsole);

        headerLabel = CreateText("HeaderLabel", headerRect,
            "► Tank Console", 14, Txt, TextAnchor.MiddleLeft);
        RectTransform labelRect = headerLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10, 0);
        labelRect.offsetMax = new Vector2(-10, 0);

        // Body (collapsible)
        bodyRect = CreateRect("Body", panelRect,
            new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(0, 0),
            new Vector2(0, -HeaderHeight));
        AddImage(bodyRect, PanelBg);

        float yOffset = -Pad;

        // ── Script input area ──
        yOffset = BuildScriptArea(bodyRect, yOffset);

        // ── Toolbar ──
        yOffset = BuildToolbar(bodyRect, yOffset);

        // ── Console output ──
        BuildConsoleOutput(bodyRect, yOffset);

        UpdateButtonStates();
    }

    float BuildScriptArea(RectTransform parent, float yTop)
    {
        // Background
        RectTransform inputBg = CreateRect("ScriptBg", parent,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(Pad, yTop - ScriptAreaHeight),
            new Vector2(-Pad, yTop));
        AddImage(inputBg, InputBg);

        // InputField
        GameObject inputGo = inputBg.gameObject;
        scriptInput = inputGo.AddComponent<InputField>();
        scriptInput.lineType = InputField.LineType.MultiLineNewline;
        scriptInput.characterLimit = 4096;
        scriptInput.text = defaultScript;

        // Child text for InputField
        Text inputText = CreateText("InputText", inputBg,
            "", 13, Txt, TextAnchor.UpperLeft);
        RectTransform itRect = inputText.rectTransform;
        itRect.anchorMin = Vector2.zero;
        itRect.anchorMax = Vector2.one;
        itRect.offsetMin = new Vector2(6, 4);
        itRect.offsetMax = new Vector2(-6, -4);
        inputText.supportRichText = false;

        // Placeholder
        Text placeholder = CreateText("Placeholder", inputBg,
            "Enter TankScript here...", 13, TxtDim, TextAnchor.UpperLeft);
        RectTransform phRect = placeholder.rectTransform;
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(6, 4);
        phRect.offsetMax = new Vector2(-6, -4);
        placeholder.fontStyle = FontStyle.Italic;

        scriptInput.textComponent = inputText;
        scriptInput.placeholder = placeholder;

        return yTop - ScriptAreaHeight - Pad;
    }

    float BuildToolbar(RectTransform parent, float yTop)
    {
        RectTransform toolbar = CreateRect("Toolbar", parent,
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(Pad, yTop - ToolbarHeight),
            new Vector2(-Pad, yTop));

        float btnW = 80f;
        float toggleW = 90f;
        float gap = 6f;
        float x = 0f;

        // Play button
        playBtn = CreateButton("PlayBtn", toolbar, "▶ Play", PlayGreen, Txt,
            new Vector2(x, 0), new Vector2(x + btnW, ToolbarHeight));
        playBtn.onClick.AddListener(Play);
        x += btnW + gap;

        // Stop button
        stopBtn = CreateButton("StopBtn", toolbar, "■ Stop", StopRed, Txt,
            new Vector2(x, 0), new Vector2(x + btnW, ToolbarHeight));
        stopBtn.onClick.AddListener(Stop);
        x += btnW + gap;

        // Loop toggle
        GameObject toggleGo = new GameObject("LoopToggle");
        toggleGo.transform.SetParent(toolbar, false);
        RectTransform toggleRect = toggleGo.AddComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0, 0);
        toggleRect.anchorMax = new Vector2(0, 1);
        toggleRect.offsetMin = new Vector2(x, 0);
        toggleRect.offsetMax = new Vector2(x + toggleW, 0);

        Image toggleBg = AddImage(toggleRect, LoopBlue);
        toggleBg.color = BtnNormal;

        loopToggle = toggleGo.AddComponent<Toggle>();
        loopToggle.isOn = false;
        loopToggle.onValueChanged.AddListener(OnLoopChanged);

        // Toggle checkmark (small square)
        RectTransform checkBg = CreateRect("CheckBg", toggleRect,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(6, -8), new Vector2(22, 8));
        AddImage(checkBg, InputBg);

        RectTransform checkmark = CreateRect("Checkmark", checkBg,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(-5, -5), new Vector2(5, 5));
        Image checkImg = AddImage(checkmark, LogGreen);
        loopToggle.graphic = checkImg;

        // Toggle label
        Text toggleLabel = CreateText("LoopLabel", toggleRect,
            "Loop", 13, Txt, TextAnchor.MiddleLeft);
        RectTransform tlRect = toggleLabel.rectTransform;
        tlRect.anchorMin = Vector2.zero;
        tlRect.anchorMax = Vector2.one;
        tlRect.offsetMin = new Vector2(26, 0);
        tlRect.offsetMax = new Vector2(0, 0);

        // Clear button (right side)
        float clearW = 70f;
        RectTransform clearBtnRect = CreateRect("ClearBtnAnchor", toolbar,
            new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(-clearW, 0), new Vector2(0, 0));

        Button clearBtn = CreateButton("ClearBtn", toolbar, "Clear", BtnNormal, TxtDim,
            new Vector2(-clearW, 0), new Vector2(0, ToolbarHeight));
        // re-anchor to right
        clearBtn.GetComponent<RectTransform>().anchorMin = new Vector2(1, 0);
        clearBtn.GetComponent<RectTransform>().anchorMax = new Vector2(1, 0);
        clearBtn.onClick.AddListener(ClearConsole);

        Destroy(clearBtnRect.gameObject); // cleanup temp anchor

        return yTop - ToolbarHeight - Pad;
    }

    void BuildConsoleOutput(RectTransform parent, float yTop)
    {
        // ScrollView
        RectTransform scrollArea = CreateRect("ConsoleScroll", parent,
            new Vector2(0, 0), new Vector2(1, 1),
            new Vector2(Pad, Pad),
            new Vector2(-Pad, yTop));
        AddImage(scrollArea, ConsoleBg);

        scrollRect = scrollArea.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        // Viewport
        RectTransform viewport = CreateRect("Viewport", scrollArea,
            Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        // Content
        RectTransform content = CreateRect("Content", viewport,
            new Vector2(0, 1), new Vector2(1, 1),
            Vector2.zero, Vector2.zero);
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;

        // Text
        consoleText = CreateText("ConsoleText", content,
            "", 12, LogGreen, TextAnchor.LowerLeft);
        consoleText.supportRichText = true;
        RectTransform ctRect = consoleText.rectTransform;
        ctRect.anchorMin = Vector2.zero;
        ctRect.anchorMax = Vector2.one;
        ctRect.offsetMin = new Vector2(6, 4);
        ctRect.offsetMax = new Vector2(-6, -4);
        ctRect.pivot = new Vector2(0, 0);

        // Size fitter on text so content grows
        ContentSizeFitter textFitter = consoleText.gameObject.AddComponent<ContentSizeFitter>();
        textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void ClearConsole()
    {
        logLines.Clear();
        if (consoleText != null)
            consoleText.text = "";
    }

    void OnLoopChanged(bool value)
    {
        looping = value;
        if (value)
            Log("Loop enabled.", TxtDim);
        else
            Log("Loop disabled.", TxtDim);
    }

    // ═══════════════════════════════════════════════════════════════
    //  UI Helpers
    // ═══════════════════════════════════════════════════════════════

    static RectTransform CreateRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt;
    }

    static Image AddImage(RectTransform rt, Color color)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    static Text CreateText(string name, RectTransform parent,
        string content, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Text txt = go.AddComponent<Text>();
        txt.text = content;
        txt.fontSize = fontSize;
        txt.color = color;
        txt.alignment = alignment;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (txt.font == null)
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        return txt;
    }

    static Button CreateButton(string name, RectTransform parent,
        string label, Color bgColor, Color textColor,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform btnRect = CreateRect(name, parent,
            new Vector2(0, 0), new Vector2(0, 0),
            offsetMin, offsetMax);
        Image bg = AddImage(btnRect, bgColor);

        Button btn = btnRect.gameObject.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = bgColor;
        cb.highlightedColor = bgColor * 1.2f;
        cb.pressedColor = bgColor * 0.8f;
        cb.disabledColor = bgColor * 0.4f;
        btn.colors = cb;
        btn.targetGraphic = bg;

        Text btnText = CreateText(name + "Label", btnRect,
            label, 13, textColor, TextAnchor.MiddleCenter);

        return btn;
    }
}
