using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the room code, local IP address, and connected player list on the host screen.
/// Attach to a GameObject in the scene alongside WebSocketServer and GameSessionManager.
/// The entire UI is built at runtime.
/// </summary>
public class RoomCodeUI : MonoBehaviour
{
    [Header("Layout")]
    public float panelWidth = 320f;
    public int fontSize = 18;

    // ── ui handles ──
    Text roomCodeText;
    Text ipText;
    Text playerListText;

    // ── style ──
    static readonly Color PanelBg = new Color(0.08f, 0.08f, 0.10f, 0.85f);
    static readonly Color HeaderColor = new Color(0.95f, 0.85f, 0.30f, 1f);
    static readonly Color CodeColor = new Color(0.50f, 0.95f, 0.50f, 1f);
    static readonly Color TextColor = new Color(0.80f, 0.85f, 0.80f, 1f);
    static readonly Color DimColor = new Color(0.55f, 0.60f, 0.55f, 1f);

    void Start()
    {
        BuildUI();
    }

    void Update()
    {
        var server = WebSocketServer.Instance;
        if (server == null || !server.IsRunning)
        {
            if (roomCodeText != null) roomCodeText.text = "Server not running";
            return;
        }

        // update room code
        if (roomCodeText != null)
            roomCodeText.text = server.RoomCode;

        // update IP
        if (ipText != null)
            ipText.text = $"{GetLocalIP()}:{server.port}";

        // update player list
        if (playerListText != null)
        {
            var clients = server.GetAllClients();
            if (clients.Count == 0)
            {
                playerListText.text = "<color=#" + ColorUtility.ToHtmlStringRGB(DimColor) + ">Waiting for players...</color>";
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var c in clients)
                {
                    string name = string.IsNullOrEmpty(c.PlayerName) ? "Connecting..." : c.PlayerName;
                    string colorHex = ColorUtility.ToHtmlStringRGB(c.TankColor);
                    string pn = c.PlayerNumber > 0 ? $"P{c.PlayerNumber}" : "?";
                    sb.AppendLine($"<color=#{colorHex}>■</color> {pn} — {name}");
                }
                playerListText.text = sb.ToString().TrimEnd();
            }
        }
    }

    void BuildUI()
    {
        // Canvas
        var canvasGo = new GameObject("RoomCodeCanvas");
        canvasGo.transform.SetParent(transform);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel — top-right corner
        var panelGo = CreatePanel(canvasGo.transform, "RoomCodePanel", panelWidth, 200f);
        var panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = new Vector2(-15, -15);

        // Vertical layout
        var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 10);
        vlg.spacing = 4;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var fitter = panelGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Header
        CreateLabel(panelGo.transform, "HeaderLabel", "ROOM CODE", fontSize - 2, HeaderColor);

        // Room code (large)
        roomCodeText = CreateLabel(panelGo.transform, "RoomCodeValue", "----", fontSize + 16, CodeColor);
        roomCodeText.fontStyle = FontStyle.Bold;
        roomCodeText.alignment = TextAnchor.MiddleCenter;

        // Separator
        CreateLabel(panelGo.transform, "Sep1", "─────────────", fontSize - 6, DimColor);

        // IP label
        CreateLabel(panelGo.transform, "IPLabel", "Connect to:", fontSize - 4, DimColor);
        ipText = CreateLabel(panelGo.transform, "IPValue", "...", fontSize - 2, TextColor);
        ipText.alignment = TextAnchor.MiddleCenter;

        // Separator
        CreateLabel(panelGo.transform, "Sep2", "─────────────", fontSize - 6, DimColor);

        // Players header
        CreateLabel(panelGo.transform, "PlayersLabel", "PLAYERS", fontSize - 2, HeaderColor);

        // Player list
        playerListText = CreateLabel(panelGo.transform, "PlayerList", "Waiting for players...", fontSize - 2, TextColor);
        playerListText.color = DimColor;
    }

    static GameObject CreatePanel(Transform parent, string name, float width, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(width, height);

        var img = go.AddComponent<Image>();
        img.color = PanelBg;

        return go;
    }

    static Text CreateLabel(Transform parent, string name, string text, int size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = true;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = size + 8;

        return t;
    }

    static string GetLocalIP()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    return ip.ToString();
            }
        }
        catch { /* ignore */ }
        return "127.0.0.1";
    }
}
