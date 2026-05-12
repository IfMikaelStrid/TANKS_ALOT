// ═══════════════════════════════════════════════════════════════
//  Message types — mirrors Unity NetworkMessages.cs
// ═══════════════════════════════════════════════════════════════

export interface NetworkEnvelope {
  type: string;
  payload: string; // JSON string
}

// ── Client → Server ──

export interface JoinRequest {
  roomCode: string;
  playerName: string;
}

export interface UpdateSettingsRequest {
  colorR: number;
  colorG: number;
  colorB: number;
  defaultScript: string;
}

export interface SubmitScriptRequest {
  script: string;
}

export interface HeartbeatMessage {
  timestamp: number;
}

// ── Server → Client ──

export interface JoinResponse {
  success: boolean;
  playerNumber: number;
  error: string;
}

export interface GameStateMessage {
  roundNumber: number;
  timeRemaining: number;
  health: number;
  maxHealth: number;
  roundActive: boolean;
  timerPaused: boolean;
  alivePlayerCount: number;
  gameMode: string;
}

export interface RoundStartedMessage {
  roundNumber: number;
}

export interface RoundEndedMessage {
  roundNumber: number;
  winnerPlayerNumber: number;
}

export interface GameOverMessage {
  winnerPlayerNumber: number;
  roundWins: PlayerWins[];
}

export interface PlayerWins {
  playerNumber: number;
  wins: number;
}

export interface ReactiveIntervalMessage {
  playerNumber: number;
}

export interface ScriptErrorMessage {
  message: string;
  line: number;
}

export interface PlayerListMessage {
  players: PlayerInfo[];
}

export interface PlayerInfo {
  playerNumber: number;
  name: string;
  colorR: number;
  colorG: number;
  colorB: number;
  alive: boolean;
  connected: boolean;
}

export interface CommandLogMessage {
  text: string;
  level: "info" | "success" | "warning" | "error";
}

export interface TankDestroyedMessage {
  playerNumber: number;
}

// ── Helpers ──

export function wrapMessage<T>(type: string, payload: T): string {
  const envelope: NetworkEnvelope = {
    type,
    payload: JSON.stringify(payload),
  };
  return JSON.stringify(envelope);
}

export function unwrapMessage(raw: string): { type: string; payload: unknown } {
  const envelope: NetworkEnvelope = JSON.parse(raw);
  return {
    type: envelope.type,
    payload: JSON.parse(envelope.payload),
  };
}
