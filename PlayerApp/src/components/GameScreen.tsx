import { useCallback, useEffect, useRef, useState } from "react";
import Editor, { type OnMount } from "@monaco-editor/react";
import { HexColorPicker } from "react-colorful";
import { registerTankScript } from "../languages/tankscript";
import type { GameState, LogEntry } from "../hooks/useWebSocket";
import type { PlayerInfo, ScriptErrorMessage, GameOverMessage, RoundEndedMessage } from "../types/messages";
import type * as monacoNs from "monaco-editor";

// ═══════════════════════════════════════════════════════════════
//  Props
// ═══════════════════════════════════════════════════════════════

interface GameScreenProps {
  playerNumber: number;
  gameState: GameState;
  players: PlayerInfo[];
  logs: LogEntry[];
  scriptError: ScriptErrorMessage | null;
  reactiveInterval: boolean;
  roundResult: RoundEndedMessage | null;
  gameOver: GameOverMessage | null;
  onSubmitScript: (script: string) => void;
  onUpdateSettings: (r: number, g: number, b: number, defaultScript: string) => void;
  onDisconnect: () => void;
}

// ═══════════════════════════════════════════════════════════════
//  Helpers
// ═══════════════════════════════════════════════════════════════

function formatTime(seconds: number): string {
  const s = Math.max(0, Math.ceil(seconds));
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${m}:${sec.toString().padStart(2, "0")}`;
}

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const result = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  if (!result) return { r: 0, g: 0, b: 1 };
  return {
    r: parseInt(result[1], 16) / 255,
    g: parseInt(result[2], 16) / 255,
    b: parseInt(result[3], 16) / 255,
  };
}

function rgbToHex(r: number, g: number, b: number): string {
  const toHex = (v: number) => Math.round(v * 255).toString(16).padStart(2, "0");
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

const LOG_COLORS: Record<string, string> = {
  info: "#d4d4d4",
  success: "#80cc80",
  warning: "#f0cc40",
  error: "#f06060",
};

const DEFAULT_SCRIPT = `FOR 4
  MOVE 5
  TURN 90
END`;

// ═══════════════════════════════════════════════════════════════
//  Component
// ═══════════════════════════════════════════════════════════════

export function GameScreen({
  playerNumber,
  gameState,
  players,
  logs,
  scriptError,
  reactiveInterval,
  roundResult,
  gameOver,
  onSubmitScript,
  onUpdateSettings,
  onDisconnect,
}: GameScreenProps) {
  const [script, setScript] = useState(DEFAULT_SCRIPT);
  const [showSettings, setShowSettings] = useState(false);
  const [tankColor, setTankColor] = useState("#3388ff");
  const [defaultScript, setDefaultScript] = useState(DEFAULT_SCRIPT);
  const logEndRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<monacoNs.editor.IStandaloneCodeEditor | null>(null);
  const monacoRef = useRef<typeof monacoNs | null>(null);

  // auto-scroll log
  useEffect(() => {
    logEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [logs]);

  // set error markers in Monaco
  useEffect(() => {
    if (!editorRef.current || !monacoRef.current) return;
    const model = editorRef.current.getModel();
    if (!model) return;

    if (scriptError && scriptError.line >= 0) {
      monacoRef.current.editor.setModelMarkers(model, "tankscript", [
        {
          severity: monacoRef.current.MarkerSeverity.Error,
          message: scriptError.message,
          startLineNumber: scriptError.line,
          startColumn: 1,
          endLineNumber: scriptError.line,
          endColumn: model.getLineMaxColumn(scriptError.line),
        },
      ]);
    } else {
      monacoRef.current.editor.setModelMarkers(model, "tankscript", []);
    }
  }, [scriptError]);

  const handleEditorMount: OnMount = useCallback((editor, monaco) => {
    editorRef.current = editor;
    monacoRef.current = monaco;
    registerTankScript(monaco);
    editor.focus();
  }, []);

  function handlePlay() {
    onSubmitScript(script);
  }

  function handleSaveSettings() {
    const { r, g, b } = hexToRgb(tankColor);
    onUpdateSettings(r, g, b, defaultScript);
    setShowSettings(false);
  }

  // find current player's color from player list
  const myPlayer = players.find((p) => p.playerNumber === playerNumber);
  useEffect(() => {
    if (myPlayer) {
      setTankColor(rgbToHex(myPlayer.colorR, myPlayer.colorG, myPlayer.colorB));
    }
  }, [myPlayer]);

  const isTimerLow = gameState.timeRemaining <= 10 && gameState.roundActive;

  return (
    <div className="game-screen">
      {/* ── Top bar ── */}
      <header className="top-bar">
        <div className="top-bar-left">
          <span className="player-badge" style={{ borderColor: tankColor }}>
            P{playerNumber}
          </span>
          <span className="mode-badge">{gameState.gameMode}</span>
        </div>
        <div className="top-bar-center">
          {gameState.roundActive && (
            <span className={`timer ${isTimerLow ? "timer-low" : ""}`}>
              {formatTime(gameState.timeRemaining)}
            </span>
          )}
          {!gameState.roundActive && gameState.roundNumber > 0 && (
            <span className="timer timer-paused">Round over</span>
          )}
        </div>
        <div className="top-bar-right">
          <button className="icon-btn" onClick={() => setShowSettings(!showSettings)} title="Settings">
            ⚙
          </button>
          <button className="icon-btn disconnect-btn" onClick={onDisconnect} title="Disconnect">
            ✕
          </button>
        </div>
      </header>

      {/* ── Reactive interval banner ── */}
      {reactiveInterval && (
        <div className="reactive-banner">
          ⏸ Reactive interval — update your script and press Play
        </div>
      )}

      {/* ── Game over banner ── */}
      {gameOver && (
        <div className="gameover-banner">
          <strong>GAME OVER</strong> —{" "}
          {gameOver.winnerPlayerNumber > 0
            ? `Player ${gameOver.winnerPlayerNumber} wins!`
            : "No winner."}
          <div className="gameover-wins">
            {gameOver.roundWins.map((pw) => (
              <span key={pw.playerNumber}>
                P{pw.playerNumber}: {pw.wins}W{" "}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* ── Round result inline ── */}
      {roundResult && !gameOver && (
        <div className="round-result-banner">
          Round {roundResult.roundNumber} —{" "}
          {roundResult.winnerPlayerNumber > 0
            ? `Player ${roundResult.winnerPlayerNumber} wins`
            : "Draw"}
        </div>
      )}

      {/* ── Main content ── */}
      <div className="main-area">
        {/* ── Left: Editor ── */}
        <div className="editor-pane">
          <div className="editor-header">
            <span>TankScript</span>
            <button className="play-btn" onClick={handlePlay}>
              ▶ Play
            </button>
          </div>
          <div className="editor-container">
            <Editor
              defaultLanguage="tankscript"
              defaultValue={DEFAULT_SCRIPT}
              theme="tankscript-dark"
              value={script}
              onChange={(val) => setScript(val ?? "")}
              onMount={handleEditorMount}
              beforeMount={(monaco) => registerTankScript(monaco)}
              options={{
                minimap: { enabled: false },
                fontSize: 15,
                lineNumbers: "on",
                scrollBeyondLastLine: false,
                wordWrap: "on",
                automaticLayout: true,
                tabSize: 2,
                renderLineHighlight: "line",
                overviewRulerLanes: 0,
                hideCursorInOverviewRuler: true,
                scrollbar: { verticalScrollbarSize: 8, horizontalScrollbarSize: 8 },
              }}
            />
          </div>
        </div>

        {/* ── Right: Status ── */}
        <div className="status-pane">
          {/* Health */}
          <div className="status-section">
            <div className="status-label">Health</div>
            <div className="health-bar">
              {Array.from({ length: gameState.maxHealth }).map((_, i) => (
                <span
                  key={i}
                  className={`health-pip ${i < gameState.health ? "health-pip-full" : "health-pip-empty"}`}
                />
              ))}
            </div>
          </div>

          {/* Round info */}
          <div className="status-section">
            <div className="status-label">Round</div>
            <div className="status-value">{gameState.roundNumber || "—"}</div>
          </div>

          {/* Alive */}
          <div className="status-section">
            <div className="status-label">Alive</div>
            <div className="status-value">{gameState.alivePlayerCount}</div>
          </div>

          {/* Players */}
          <div className="status-section">
            <div className="status-label">Players</div>
            <div className="player-list">
              {players.map((p) => (
                <div
                  key={p.playerNumber}
                  className={`player-row ${!p.alive ? "player-dead" : ""}`}
                >
                  <span
                    className="player-dot"
                    style={{ backgroundColor: rgbToHex(p.colorR, p.colorG, p.colorB) }}
                  />
                  <span className="player-name">
                    P{p.playerNumber} {p.name}
                  </span>
                  {!p.alive && <span className="dead-marker">✕</span>}
                </div>
              ))}
            </div>
          </div>

          {/* Console log */}
          <div className="status-section console-section">
            <div className="status-label">Console</div>
            <div className="console-log">
              {logs.map((entry, i) => (
                <div key={i} className="log-line" style={{ color: LOG_COLORS[entry.level] }}>
                  {entry.text}
                </div>
              ))}
              <div ref={logEndRef} />
            </div>
          </div>
        </div>
      </div>

      {/* ── Settings overlay ── */}
      {showSettings && (
        <div className="settings-overlay" onClick={() => setShowSettings(false)}>
          <div className="settings-panel" onClick={(e) => e.stopPropagation()}>
            <h2>Settings</h2>
            <div className="settings-group">
              <label>Tank Color</label>
              <HexColorPicker color={tankColor} onChange={setTankColor} />
              <div className="color-preview" style={{ backgroundColor: tankColor }} />
            </div>
            <div className="settings-group">
              <label>Default Script</label>
              <textarea
                className="default-script-input"
                value={defaultScript}
                onChange={(e) => setDefaultScript(e.target.value)}
                rows={5}
              />
            </div>
            <div className="settings-actions">
              <button className="save-btn" onClick={handleSaveSettings}>
                Save
              </button>
              <button className="cancel-btn" onClick={() => setShowSettings(false)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
