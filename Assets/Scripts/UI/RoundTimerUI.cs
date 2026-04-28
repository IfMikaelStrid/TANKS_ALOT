using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the round countdown timer at the top-center of the screen.
/// Attach to any GameObject. The UI is built at runtime.
/// </summary>
public class RoundTimerUI : MonoBehaviour
{
    [Header("Style")]
    public int fontSize = 42;
    public Color textColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    public Color warningColor = new Color(0.95f, 0.40f, 0.35f, 1f);
    public float warningThreshold = 10f;
    public Color bgColor = new Color(0.08f, 0.08f, 0.10f, 0.75f);

    Text timerText;
    Image bgImage;
    Canvas canvas;
    bool showing;

    void Awake()
    {
        BuildUI();
        SetVisible(false);
    }

    void OnEnable()
    {
        TankEventBus.OnRoundStarted += HandleRoundStarted;
        TankEventBus.OnRoundEnded += HandleRoundEnded;
        TankEventBus.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        TankEventBus.OnRoundStarted -= HandleRoundStarted;
        TankEventBus.OnRoundEnded -= HandleRoundEnded;
        TankEventBus.OnGameOver -= HandleGameOver;
    }

    void Update()
    {
        if (!showing) return;

        var gm = GameManager.Instance;
        if (gm == null || !gm.RoundActive)
            return;

        float t = Mathf.Max(0f, gm.RoundTimeRemaining);
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);

        timerText.text = minutes > 0
            ? string.Format("{0}:{1:00}", minutes, seconds)
            : string.Format("0:{0:00}", seconds);

        timerText.color = t <= warningThreshold ? warningColor : textColor;
    }

    void HandleRoundStarted(int roundNumber)
    {
        SetVisible(true);
    }

    void HandleRoundEnded(int roundNumber, int winner)
    {
        SetVisible(false);
    }

    void HandleGameOver(int winner)
    {
        SetVisible(false);
    }

    void SetVisible(bool visible)
    {
        showing = visible;
        if (canvas != null)
            canvas.gameObject.SetActive(visible);
    }

    void BuildUI()
    {
        // Canvas
        GameObject canvasGo = new GameObject("RoundTimerCanvas");
        canvasGo.transform.SetParent(transform);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Background panel – anchored top-center
        GameObject panelGo = new GameObject("TimerPanel");
        panelGo.transform.SetParent(canvasGo.transform, false);
        RectTransform panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -10f);
        panelRect.sizeDelta = new Vector2(180f, 60f);

        bgImage = panelGo.AddComponent<Image>();
        bgImage.color = bgColor;

        // Timer text
        GameObject textGo = new GameObject("TimerText");
        textGo.transform.SetParent(panelGo.transform, false);
        timerText = textGo.AddComponent<Text>();
        timerText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        timerText.fontSize = fontSize;
        timerText.alignment = TextAnchor.MiddleCenter;
        timerText.color = textColor;
        timerText.text = "0:00";

        RectTransform textRect = timerText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
}
