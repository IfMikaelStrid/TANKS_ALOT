# Multiplayer Setup (Tanks)

Refactor adds Unity Relay-based hosting/joining via code, server-authoritative tanks, capacity 8 players (1 host + 7), passive mode only.

## Packages (already in `Packages/manifest.json`)
- `com.unity.netcode.gameobjects` 2.11
- `com.unity.services.multiplayer` 2.2 (provides Auth + Relay + AllocationUtils)
- `com.unity.transport` 2.6 (NGO transport)

## One-time scene setup

### 1. Bootstrap scene (`StartMenu`)
Add a single empty GameObject `[Bootstrap]` with:
- `NetworkManager` component
  - **Network Transport**: `UnityTransport`
  - **Network Prefabs List**: drag `Assets/DefaultNetworkPrefabs.asset`
  - **Tick Rate**: 30 (or default)
- `RelayBootstrap` component
  - `gameplayScene = "LevelOne"` (the scene the host will load after starting)
- `MultiplayerMenuUI` component (auto-builds the host/join UI at runtime)

Mark `[Bootstrap]` `DontDestroyOnLoad` is handled in code.

### 2. Gameplay scene (`LevelOne` / `Sandbox`)
Required objects:
- `[NetworkSpawner]` empty GameObject:
  - `NetworkObject` (component)
  - `NetworkPlayerSpawner` (set `tankPrefab` to your tank prefab)
- `[GameManager]` empty GameObject:
  - `NetworkObject`
  - `GameManager` (set `gameMode = Passive`)
- One or more `PlayerStart` GameObjects (one per slot, up to 8). Set `playerNumber` 1..8 and `tankColor`.
- `RoundTimerUI` and any other UI (unchanged).

Add both `[NetworkSpawner]` and `[GameManager]` to **Network Prefabs List** ONLY if you spawn them dynamically. If they live in the scene, no prefab entry is needed — but they MUST be marked as scene-placed `NetworkObject`s and the scene must be loaded via `NetworkManager.SceneManager.LoadScene` (handled by `RelayBootstrap`).

### 3. Tank prefab
Open your existing tank prefab and add:
- `NetworkObject` component (top-level)
- `NetworkTransform` component (set Authority = Server). This syncs position/rotation to clients.
- Existing `InputListener`, `TankHealth`, `TankFiring`, `LineOfSightCone`, `TankUprightCorrector` already updated to be network-aware.
- Drag tank prefab into `Assets/DefaultNetworkPrefabs.asset` list.

### 4. Bullet prefab
Add to bullet prefab:
- `NetworkObject` (so explosions sync)
- `NetworkTransform` (Server authority)
- Existing `ShellExplosion` is now network-aware.
- Drag bullet prefab into `DefaultNetworkPrefabs.asset`.

## Runtime flow
1. Player launches game → `StartMenu` scene → menu UI shown.
2. Click **Host Game** → Unity Services init + anonymous sign-in + Relay allocation → join code displayed → `NetworkManager.StartHost()` → `LevelOne` loaded for host (clients follow).
3. Other player launches game → enters join code → **Join** → joins host's scene.
4. `NetworkPlayerSpawner` (server-only) spawns one tank per client at the next free `PlayerStart`, assigns ownership.
5. Each client's `TankConsoleUI` finds the locally-owned tank, sends `SubmitScriptServerRpc(text)`. Server parses and runs the script in a passive loop, dispatching movement on the server. `NetworkTransform` syncs to clients.
6. Damage/health is server-authoritative; `TankHealth.TakeDamage` only mutates on server.
7. Round/game events broadcast to all clients via `ClientRpc → TankEventBus`.

## Capacity
- `RelayBootstrap.MaxPlayers = 8` (1 host + 7 clients).
- `NetworkPlayerSpawner` enforces by kicking extras and finding a free `PlayerStart` per join.

## Game mode
- Multiplayer is locked to `GameMode.Passive` (logged warning if set otherwise).
- `Active`/`Reactive`/`Dev` paths kept intact for offline/single-player testing only (run scene without launching the menu, and `NetworkManager` won't be listening).

## Notes / known limits
- Auto-generated `Assembly-CSharp.csproj` may list ghost files from earlier multiplayer attempts (`RelayManager.cs`, `NetworkPlayer.cs`, etc.). Reload the project in Unity once — it regenerates the csproj from disk.
- Anonymous sign-in is used. To enable production auth, switch to `SignInWithUsernamePasswordAsync` or another provider in `RelayBootstrap.EnsureSignedIn`.
- `TankTestDriver` still works offline; in network play it should be removed/disabled on the tank prefab to avoid double-driving.
