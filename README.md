<p align="center">
  <img src="assets/logo.png" alt="Emberward Co-op" width="420">
</p>

# Emberward Co-op

Online P2P and Direct LAN co-op mod (2–4 players) for **Emberward**, built with BepInEx 6 (IL2CPP) and Harmony.

Hosts can run sessions over Steam P2P or Direct LAN in **Campaign**, **Endless Mode**, or **Enigma Sanctum**, using either a **Shared Resources** pool or **Separate Resources** with per-player economies and tower ownership. Co-op runs stay isolated in their own session with dedicated save files (`CoopSaves/`), leaving single-player active runs untouched while still writing earned meta-progression unlocks to both host and client solo profiles.

---

## Features

- **Online Steam P2P & Direct LAN:** Host Friends-Only, Public, or Private Steam lobbies with numeric lobby codes and Steam invite overlay support, or host directly over LAN (TCP + UDP) with automatic LAN discovery. Lobby queries filter strictly for valid Emberward Co-op sessions.
- **Supported Game Modes:** Play **Campaign** (Worlds 1–5 and Chaos Realm across Casual, Normal, Heroic, and Nightmare), **Endless Mode** (stage pools per world plus Random Endless Challenge), and **Enigma Sanctum**.
- **Shared vs. Separate Resources:**
  - **Shared Resources:** Unified gold, energy, core health, towers, cards, and relics. Vanilla Academy, reward, shop, chest, altar, and campsite popups open across the party with deterministic shared seeds, live client voting, reroll requests, and host-synced picks.
  - **Separate Resources:** Independent gold, hands, decks, and relics, strict per-player tower ownership (only the placing player can upgrade their towers, while teammates can still inspect tower stats with an `"Owned by [Player]"` label), mid-run gold gifting, and simultaneous independent vanilla drafts/shops with deterministic per-player seeds plus a drafting barrier so nobody is pulled out mid-pick.
  - **Core Health** is always a single shared pool across the party in both modes.
- **Per-Player Block Limits & Tower Outlines:**
  - Block placement caps track each player individually (default **3 blocks per player per turn**, configurable in Session Settings), synced across the network, persisted in co-op saves, and displayed in the Players HUD.
  - Placed towers display crisp player-colored 3D outlines matching the placing player's chosen color (persisting through tower upgrades and save/load, updating live on color changes, and configurable in Mod Settings).
- **Host-Only Level Transitions & Upgrade Gate:** Only the host can trigger level exits, continues, retries, or Endless transitions; client transition buttons lock with a `"Waiting for Host..."` state. After a stage win, the host's leave-level buttons remain locked until all connected clients finish their post-level upgrade and reward popups (or disconnect).
- **Vanilla Hero & Progression Flow:**
  - Run hero and ember selection uses Emberward's in-game Home screen (selected and confirmed by the host, synced live to clients), followed by the vanilla Academy draft.
  - Selectable modes, worlds, and difficulties resolve from the **most-progressed player in the lobby**. An optional **Unlock All Maps/Modes (Co-op Only)** toggle in Mod Settings applies strictly in memory inside the Co-op Lobby and active co-op runs without modifying solo savefiles.
- **Standalone Co-op Saves & Solo Meta-Progression:** Co-op never loads or overwrites single-player active-run save slots on either host or clients. Hosts can start a new run or resume a saved co-op run from the Pre-Run Lobby (`NEW CO-OP RUN` / `LOAD CO-OP RUN`), with automatic saves on map entry, stage clears, wave ends, and exit to menu, plus a manual **SAVE CO-OP RUN** button in the pause menu. Earned XP, stage/world clear records, boss clear flags, codex entries, and achievements commit to both host and client solo profiles.
- **Map Voting & Session Rules:** Both host and clients vote on available map nodes with live vote chips and automatic consensus/tie-break resolution. Hosts can configure enemy HP, wave density, enemy speed, boss HP/armor, starting gold split, wave income scaling, per-player turn block caps, shared core HP bonus, gold gifting caps, draft sync mode, and speed control permissions. Includes four built-in presets (*Balanced Co-op*, *Casual*, *Tactical Hardcore*, *Chaos*) and JSON preset save/load.
- **Presence & Tactical Tools:** Color-coded player names (portraits stay untinted), Lobby Hero Avatar selector, remote player cursors, translucent ghost previews of hovered/rotated blocks and towers, contextual pings and a 5-slice ping wheel, grid-snapped maze path sketching, a 10-sticker emote wheel with 6 quick callouts, a collapsible Players HUD with inline latency readouts, remaining block counters, and hover tooltips, and an auto-hiding social chat log. Camera panning/zooming and board clicks lock while radial wheels, sketching, or co-op modals are active.
- **Sync Safety & Post-Run Breakdown:** Authoritative tower upgrade sync across host and clients, periodic state-hash checks with automatic resync, mid-wave auto-pause modal on peer disconnect, and a post-run contribution summary on victory and defeat screens.
- **Mod Settings & Localization:** Change your player color, toggle co-op-only unlocks, rebind hotkeys, and adjust HUD compactness, cursors/ghosts/outlines, sticker scale/volume, mute toggles, and disconnect auto-pause from `Pause -> CO-OP SETTINGS` or the Lobby `Mod Settings` button. Follows Emberward's active language across 10 editable JSON files in `Languages/` (`en`, `de`, `es`, `fr`, `ja`, `ko`, `pt-BR`, `ru`, `zh-CN`, `zh-TW`).

---

## Prerequisites & Requirements

| Requirement | Details |
| :--- | :--- |
| **Emberward** | Steam PC version (Unity `2022.3.62f2` IL2CPP). Tested on Steam build `26835875` (`1.1.0`). |
| **BepInEx 6 (Unity.IL2CPP x64)** | Required mod loader. Tested on [`6.0.0-be.789`](https://builds.bepinex.dev/projects/bepinex_be) (`.NET 6.0.7` IL2CPP runtime). |
| **Steam Client** | Required only for **Online (Steam P2P)** lobbies and friend invites. **Direct LAN** works without Steam. |
| **.NET 6.0+ SDK** *(Source builds only)* | Only needed if compiling from source ([Microsoft .NET Downloads](https://dotnet.microsoft.com/download)). |

### Setting Up BepInEx 6 (First-Time Only)

1. Download the latest **`BepInEx-Unity.IL2CPP-win-x64`** archive from the [BepInEx 6 Bleeding Edge Builds](https://builds.bepinex.dev/projects/bepinex_be) page.
2. Extract its contents directly into your **Emberward** root folder (the folder containing `Emberward.exe`).
3. Launch Emberward once to let BepInEx generate `BepInEx/interop/`, then close the game.

---

## Installation

All players in a session must have the same mod version installed.

1. Download **`EmberwardCoop-v1.0.0.zip`** from the [Releases](https://github.com/HyruleChosenOne/Emberward-Coop/releases) page.
2. Extract the archive directly into your **Emberward** root folder (where `Emberward.exe` is located) so the plugin files land at:
   ```text
   Emberward/
   └── BepInEx/
       └── plugins/
           └── EmberwardCoop/
               ├── EmberwardCoop.dll
               └── Languages/
                   ├── de.json
                   ├── en.json
                   ├── es.json
                   ├── fr.json
                   ├── ja.json
                   ├── ko.json
                   ├── pt-BR.json
                   ├── ru.json
                   ├── zh-CN.json
                   └── zh-TW.json
   ```
3. Launch Emberward and click **CO-OP** on the title screen (or press **`F8`**) to host or join a lobby.

> **Direct LAN ports:** Hosting on LAN uses TCP port `27777` (configurable), UDP `port + 1` for cursor/ghost updates, and UDP `27778` for automatic LAN discovery.

---

## Default Controls

Primary bindings can be changed in-game via **Pause -> CO-OP SETTINGS** or the Lobby **Mod Settings** button (saved to `BepInEx/config/com.emberward.coop.cfg`).

| Action | Default Input | Notes |
| :--- | :--- | :--- |
| **Toggle Co-op Lobby** | `F8` | Opens or closes the pre-run lobby on the title screen |
| **Toggle Players HUD** | `F7` | Collapses or expands the in-run Players HUD |
| **Emote / Sticker Wheel** | `T` | Tap to click a sticker, or hold and release on a slice; `Tab` or scroll wheel switches to Callouts (`1`–`6` send directly) |
| **Smart Ping / Ping Wheel** | `Middle Mouse` | Tap for a context-aware ping; hold to open the 5-slice tactical wheel |
| **Quick Tactical Pings** | `F1` – `F5` | Direct pings at cursor: Build Maze (`F1`), Place DPS (`F2`), Need Gold (`F3`), Upgrade/Focus (`F4`), Watch Lane (`F5`) |
| **Maze Path Sketching** | `Left Alt + Left Drag` | Draws a temporary tile-snapped directional path visible to teammates |

---

## Building from Source

1. Install the [.NET 6.0+ SDK](https://dotnet.microsoft.com/download).
2. Ensure **Emberward** has been launched at least once with **BepInEx 6 (Unity.IL2CPP x64)** so `dotnet/`, `BepInEx/core/`, and `BepInEx/interop/` exist in your game directory.
3. Point MSBuild to your Emberward installation using any of the following:
   - Copy `EmberwardCoop.csproj.user.example` to `EmberwardCoop.csproj.user` and edit `<EmberwardDir>`.
   - Set the `EmberwardDir` environment variable.
   - Pass `-p:EmberwardDir="C:\Path\To\Emberward"` on the command line.
4. Build the Release binary:
   ```powershell
   dotnet build EmberwardCoop.csproj -c Release
   ```
   The compiled `EmberwardCoop.dll` and `Languages/` folder are written to `bin/Release/`. Copy both into `<EmberwardDir>/BepInEx/plugins/EmberwardCoop/`.

---

## Credits

- **[Hyrule (`HyruleChosenOne`)](https://github.com/HyruleChosenOne)** — Mod creator and maintainer.
- **ReficGames** — Developers of *Emberward*.
- **BepInEx, HarmonyX, Il2CppInterop & MonoMod contributors** — Unity IL2CPP modding framework and runtime patching infrastructure.
- **Steamworks.NET (`rlabrecque`)** — Steamworks C# wrapper bundled with Emberward.

---

## Disclaimer & License

This is an unofficial community mod and is not affiliated with, endorsed by, or supported by ReficGames.

Licensed under the [MIT License](LICENSE).
