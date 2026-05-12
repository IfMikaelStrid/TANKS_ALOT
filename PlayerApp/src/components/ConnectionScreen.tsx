import { useState } from "react";
import type { ConnectionStatus } from "../hooks/useWebSocket";

interface ConnectionScreenProps {
  status: ConnectionStatus;
  error: string;
  onConnect: (host: string, port: number, roomCode: string, playerName: string) => void;
  clearError: () => void;
}

export function ConnectionScreen({ status, error, onConnect, clearError }: ConnectionScreenProps) {
  const [host, setHost] = useState("localhost");
  const [port, setPort] = useState("9090");
  const [roomCode, setRoomCode] = useState("");
  const [playerName, setPlayerName] = useState("");

  const isConnecting = status === "connecting";

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    clearError();

    const trimmedCode = roomCode.trim().toUpperCase();
    if (trimmedCode.length !== 4) {
      return;
    }

    const portNum = parseInt(port, 10);
    if (isNaN(portNum) || portNum < 1 || portNum > 65535) {
      return;
    }

    onConnect(host.trim(), portNum, trimmedCode, playerName.trim() || "Player");
  }

  return (
    <div className="connection-screen">
      <div className="connection-card">
        <h1 className="title">
          <span className="title-icon">⬡</span> TANKS
        </h1>
        <p className="subtitle">Enter the room code shown on the host screen</p>

        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label htmlFor="playerName">Player Name</label>
            <input
              id="playerName"
              type="text"
              value={playerName}
              onChange={(e) => setPlayerName(e.target.value)}
              placeholder="Your name"
              maxLength={20}
              autoFocus
            />
          </div>

          <div className="form-group">
            <label htmlFor="roomCode">Room Code</label>
            <input
              id="roomCode"
              type="text"
              className="room-code-input"
              value={roomCode}
              onChange={(e) => setRoomCode(e.target.value.toUpperCase().slice(0, 4))}
              placeholder="XXXX"
              maxLength={4}
              spellCheck={false}
              autoComplete="off"
            />
          </div>

          <div className="form-row">
            <div className="form-group flex-grow">
              <label htmlFor="host">Host IP</label>
              <input
                id="host"
                type="text"
                value={host}
                onChange={(e) => setHost(e.target.value)}
                placeholder="192.168.1.x"
              />
            </div>
            <div className="form-group port-group">
              <label htmlFor="port">Port</label>
              <input
                id="port"
                type="text"
                value={port}
                onChange={(e) => setPort(e.target.value)}
                placeholder="9090"
              />
            </div>
          </div>

          {error && <div className="error-banner">{error}</div>}

          <button
            type="submit"
            className="connect-btn"
            disabled={isConnecting || roomCode.trim().length !== 4}
          >
            {isConnecting ? "Connecting..." : "Connect"}
          </button>
        </form>
      </div>
    </div>
  );
}
