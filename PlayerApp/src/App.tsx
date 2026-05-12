import { ConnectionScreen } from "./components/ConnectionScreen";
import { GameScreen } from "./components/GameScreen";
import { useWebSocket } from "./hooks/useWebSocket";
import "./App.css";

export default function App() {
  const ws = useWebSocket();

  // Show connection screen until joined
  if (ws.status !== "joined") {
    return (
      <ConnectionScreen
        status={ws.status}
        error={ws.error}
        onConnect={ws.connect}
        clearError={ws.clearError}
      />
    );
  }

  return (
    <GameScreen
      playerNumber={ws.playerNumber}
      gameState={ws.gameState}
      players={ws.players}
      logs={ws.logs}
      scriptError={ws.scriptError}
      reactiveInterval={ws.reactiveInterval}
      roundResult={ws.roundResult}
      gameOver={ws.gameOver}
      onSubmitScript={ws.submitScript}
      onUpdateSettings={ws.updateSettings}
      onDisconnect={ws.disconnect}
    />
  );
}
