# Changelog

## 1.0.0

Initial public release.

### Networking, Lobby & Save System
- Added Online Steam P2P transport (SteamMatchmaking lobbies, lobby codes, Steam invite overlay, relay support) and Direct LAN transport (TCP reliable channel, UDP unreliable channel with TCP fallback, UDP broadcast auto-discovery).
- Added strict Steam matchmaking and LAN discovery filtering (game tag, mod signature, valid mode/player counts, same Steam AppID for friend lobbies, and version mismatch indicators) so only valid Emberward Co-op lobbies appear in the browser.
- Added a Pre-Run Co-op Lobby launched from the title menu (`CO-OP` button or `F8`), with host-controlled run hero selection, world/stage preview, difficulty tier selector, live-synced rules summary, party roster with inline latency readouts, Hero Avatar Selector, ready check, and lobby chat.
- Added isolated standalone co-op sessions for both host and clients so single-player active-run save slots are never loaded or overwritten, paired with a dedicated `CoopSaves/` save/load system (New Run / Load Co-op Run switcher in the lobby, automatic mid-run saving, and manual pause-menu saving).
- Added party-best map/difficulty/hero unlock resolution based on the most-progressed player in the lobby, plus a host-controlled co-op-only **Unlock All Maps/Modes** toggle that applies strictly in memory without mutating solo savefiles.

### Co-op Gameplay, Loadouts, Voting & Drafting
- Added **Shared Resources** mode (unified gold, energy, core health, and shared loadout/deck by default, with a **Shared Loadouts & Cards** toggle for individual loadouts) and **Separate Resources** mode (independent gold/hands/decks, per-player pre-run starting tower and block loadouts, per-player tower and block ownership with color rings, and gold gifting).
- Kept **Core Health** strictly unified as a single shared pool across the party in both modes.
- Added guaranteed client starting deck and tower hydration on run and battle entry so connected clients never enter Wave 1 with empty hands or tower slots.
- Added synchronized map-node voting (floating vote badges on nodes, consensus resolution, host tie-break / resolve button), shared draft voting and reroll sync in Shared Draft mode, and simultaneous deterministic per-player drafts/shops with a completion barrier in Separate/Individual mode.
- Added host-configurable difficulty and pacing rules (enemy HP, wave density, enemy speed, boss HP/armor, starting gold split, wave income multiplier, shared turn block cap, shared core HP bonus, speed control, wave start policy), built-in presets (*Balanced Co-op*, *Casual*, *Tactical Hardcore*, *Chaos*), and custom JSON preset save/load.
- Added full run, map node, board, tower, tetromino, rune stone, chest, buff, quest, speed, pause, and victory/defeat synchronization with periodic state-hash reconciliation.
- Added solo profile meta-progression persistence for both host and clients (XP, stage/world clear records, boss clear flags, tower/relic codex entries, and achievements) without touching solo active-run saves.
- Added mid-wave disconnect auto-pause modal and post-run contribution breakdown overlay.

### UI, Tactical Tools, Settings & Localization
- Built all custom menus, overlays, hero portraits, stickers, and ping markers directly from Emberward's in-game UI frames, sprites, icons, fonts, and audio.
- Styled player colors on player names only across the lobby, HUD, hover tooltips, nameplates, and post-run summary while keeping character portraits untinted, and added a host/player color swatch picker with automatic collision reassignment in Co-op Settings.
- Added remote player cursors, translucent ghost previews of hovered/rotated towers and blocks, contextual smart pings and 5-slice ping wheel, grid-snapped maze path sketching, a 10-sticker emote wheel with 6 quick callouts (`Tab` or scroll wheel switches tabs), build-phase quick undo (`Ctrl+Z`), a collapsible in-run Players HUD (`F7`) with inline ping readouts and hover previews, and an auto-hiding social chat feed.
- Added camera and world-input locking while radial wheels, maze sketching, or co-op modals are active.
- Added an in-game Co-op Mod Settings menu (accessible from the pause menu and lobby) for keybind rebinding and UI/audio toggles.
- Added external JSON localization synced to Emberward's active language for English, German, Spanish, French, Japanese, Korean, Brazilian Portuguese, Russian, Simplified Chinese, and Traditional Chinese.
