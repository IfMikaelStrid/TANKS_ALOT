import { useCallback, useEffect, useRef, useState } from "react";
import {
  type CommandLogMessage,
  type GameOverMessage,
  type GameStateMessage,
  type JoinResponse,
  type PlayerInfo,
  type ReactiveIntervalMessage,
  type RoundEndedMessage,
  type RoundStartedMessage,
  type ScriptErrorMessage,
  type TankDestroyedMessage,
  unwrapMessage,
  wrapMessage,
} from "../types/messages";

// ═══════════════════════════════════════════════════════════════
//  Connection state
// ═══════════════════════════════════════════════════════════════

export type ConnectionStatus = "disconnected" | "connecting" | "connected" | "joined";

export interface GameState {
  roundNumber: number;
  timeRemaining: number;
  health: number;
  maxHealth: number;
  roundActive: boolean;
  timerPaused: boolean;
  alivePlayerCount: number;
  gameMode: string;
}

export interface LogEntry {
  text: string;
  level: "info" | "success" | "warning" | "error";
  timestamp: number;
}

// ═══════════════════════════════════════════════════════════════
//  Hook
// ═══════════════════════════════════════════════════════════════

export function useWebSocket() {
  const wsRef = useRef<WebSocket | null>(null);
  const heartbeatRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const [status, setStatus] = useState<ConnectionStatus>("disconnected");
  const [playerNumber, setPlayerNumber] = useState(-1);
  const [error, setError] = useState("");
  const [gameState, setGameState] = useState<GameState>({
    roundNumber: 0,
    timeRemaining: 0,
    health: 3,
    maxHealth: 3,
    roundActive: false,
    timerPaused: false,
    alivePlayerCount: 0,
    gameMode: "Dev",
  });
  const [players, setPlayers] = useState<PlayerInfo[]>([]);
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [scriptError, setScriptError] = useState<ScriptErrorMessage | null>(null);
  const [reactiveInterval, setReactiveInterval] = useState(false);
  const [roundResult, setRoundResult] = useState<RoundEndedMessage | null>(null);
  const [gameOver, setGameOver] = useState<GameOverMessage | null>(null);

  // ── helpers ──

  const addLog = useCallback((text: string, level: LogEntry["level"]) => {
    setLogs((prev) => [...prev.slice(-199), { text, level, timestamp: Date.now() }]);
  }, []);

  const clearError = useCallback(() => setError(""), []);

  // ── connect ──

  const connect = useCallback(
    (host: string, port: number, roomCode: string, playerName: string) => {
      // clean up any existing connection
      if (wsRef.current) {
        wsRef.current.close();
        wsRef.current = null;
      }

      setStatus("connecting");
      setError("");
      setScriptError(null);
      setReactiveInterval(false);
      setRoundResult(null);
      setGameOver(null);
      setLogs([]);

      const url = `ws://${host}:${port}/`;
      const ws = new WebSocket(url);
      wsRef.current = ws;

      ws.onopen = () => {
        setStatus("connected");
        addLog("Connected to server.", "success");

        // send join request
        ws.send(wrapMessage("JoinRequest", { roomCode, playerName }));

        // start heartbeat
        heartbeatRef.current = setInterval(() => {
          if (ws.readyState === WebSocket.OPEN) {
            ws.send(wrapMessage("Heartbeat", { timestamp: Date.now() }));
          }
        }, 5000);
      };

      ws.onmessage = (event) => {
        try {
          const { type, payload } = unwrapMessage(event.data as string);
          handleMessage(type, payload);
        } catch {
          console.warn("Failed to parse message:", event.data);
        }
      };

      ws.onclose = () => {
        setStatus("disconnected");
        setPlayerNumber(-1);
        cleanup();
        addLog("Disconnected from server.", "warning");
      };

      ws.onerror = () => {
        setError("Connection failed. Check the host IP and port.");
        setStatus("disconnected");
        cleanup();
      };

      function handleMessage(type: string, payload: unknown) {
        switch (type) {
          case "JoinResponse": {
            const resp = payload as JoinResponse;
            if (resp.success) {
              setStatus("joined");
              setPlayerNumber(resp.playerNumber);
              addLog(`Joined as Player ${resp.playerNumber}.`, "success");
            } else {
              setError(resp.error);
              setStatus("disconnected");
              ws.close();
            }
            break;
          }
          case "GameState":
            setGameState(payload as GameStateMessage);
            break;
          case "PlayerList":
            setPlayers((payload as { players: PlayerInfo[] }).players);
            break;
          case "RoundStarted": {
            const msg = payload as RoundStartedMessage;
            setRoundResult(null);
            setReactiveInterval(false);
            addLog(`═ Round ${msg.roundNumber} started! ═`, "success");
            break;
          }
          case "RoundEnded": {
            const msg = payload as RoundEndedMessage;
            setRoundResult(msg);
            setReactiveInterval(false);
            const winner =
              msg.winnerPlayerNumber > 0
                ? `Player ${msg.winnerPlayerNumber} wins!`
                : "Draw!";
            addLog(`═ Round ${msg.roundNumber} ended. ${winner} ═`, "warning");
            break;
          }
          case "GameOver": {
            const msg = payload as GameOverMessage;
            setGameOver(msg);
            const winner =
              msg.winnerPlayerNumber > 0
                ? `Player ${msg.winnerPlayerNumber} wins the game!`
                : "No winner.";
            addLog(`══ GAME OVER. ${winner} ══`, "success");
            break;
          }
          case "ReactiveInterval": {
            const _msg = payload as ReactiveIntervalMessage;
            setReactiveInterval(true);
            addLog("⏸ Reactive interval — update your script and press Play.", "warning");
            break;
          }
          case "ScriptError": {
            const msg = payload as ScriptErrorMessage;
            setScriptError(msg);
            addLog(`Parse error: ${msg.message}`, "error");
            break;
          }
          case "CommandLog": {
            const msg = payload as CommandLogMessage;
            addLog(msg.text, msg.level);
            break;
          }
          case "TankDestroyed": {
            const msg = payload as TankDestroyedMessage;
            addLog(`Player ${msg.playerNumber} destroyed!`, "warning");
            break;
          }
          default:
            console.warn("Unknown message type:", type);
        }
      }

      function cleanup() {
        if (heartbeatRef.current) {
          clearInterval(heartbeatRef.current);
          heartbeatRef.current = null;
        }
      }
    },
    [addLog],
  );

  // ── disconnect ──

  const disconnect = useCallback(() => {
    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }
  }, []);

  // ── send helpers ──

  const submitScript = useCallback((script: string) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      setScriptError(null);
      setReactiveInterval(false);
      wsRef.current.send(wrapMessage("SubmitScript", { script }));
    }
  }, []);

  const updateSettings = useCallback(
    (colorR: number, colorG: number, colorB: number, defaultScript: string) => {
      if (wsRef.current?.readyState === WebSocket.OPEN) {
        wsRef.current.send(
          wrapMessage("UpdateSettings", { colorR, colorG, colorB, defaultScript }),
        );
      }
    },
    [],
  );

  // ── cleanup on unmount ──

  useEffect(() => {
    return () => {
      if (heartbeatRef.current) clearInterval(heartbeatRef.current);
      if (wsRef.current) wsRef.current.close();
    };
  }, []);

  return {
    status,
    playerNumber,
    error,
    clearError,
    gameState,
    players,
    logs,
    scriptError,
    reactiveInterval,
    roundResult,
    gameOver,
    connect,
    disconnect,
    submitScript,
    updateSettings,
  };
}
