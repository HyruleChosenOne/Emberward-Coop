# Changelog

## 1.0.0

Initial public release.

### Networking & Lobby
- Added Online Steam P2P transport (SteamMatchmaking lobbies, lobby codes, Steam invite overlay, relay support) and Direct LAN transport (TCP reliable channel, UDP unreliable channel with TCP fallback, UDP broadcast auto-discovery).
- Added strict Steam matchmaking and LAN discovery filtering (game tag, mod signature, valid mode/player counts, same Steam AppID for friend lobbies, and version mismatch indicators) so only valid Emberward Co-op lobbies appear in the browser.
- Added a Pre-Run Co-op Lobby launched from the title menu (`CO-OP` button or `F8`), with host-controlled world/stage preview, difficulty tier selector, live-synced rules summary, party roster with inline latency readouts, Hero Avatar Selector, ready check, and lobby chat.

### Co-op Gameplay & Rulesets
- Added **Shared Resources** mode (unified gold, energy, deck, and hand synced across all players) and **Separate Resources** mode (independent gold/hands/decks, per-player tower and block ownership with color rings, gold gifting, and independent or host-shared reward drafts and shops).
- Added host-configurable difficulty and pacing rules (enemy HP, wave density, enemy speed, boss HP/armor, starting gold split, wave income multiplier, shared turn block cap, core HP scaling, speed control, wave start policy), built-in presets (*Balanced Co-op*, *Casual*, *Tactical Hardcore*, *Chaos*), and custom JSON preset save/load.
- Added full run, map node, board, tower, tetromino, rune stone, chest, buff, quest, speed, pause, and victory/defeat synchronization with periodic state-hash reconciliation.
- Isolated client runs in a temporary session so solo saves remain untouched while XP, codex records, and achievements commit to the client's profile.
- Added mid-wave disconnect auto-pause modal and post-run contribution breakdown overlay.

### UI, Tactical Tools, Settings & Localization
- Built all custom menus, overlays, hero portraits, stickers, and ping markers directly from Emberward's in-game UI frames, sprites, icons, fonts, and audio.
- Added remote player cursors, translucent ghost previews of hovered/rotated towers and blocks, contextual smart pings and 5-slice ping wheel, grid-snapped maze path sketching, a 10-sticker emote wheel with 6 quick callouts (`Tab` or scroll wheel switches tabs), build-phase quick undo (`Ctrl+Z`), a collapsible in-run Players HUD (`F7`) with inline ping readouts and hover previews, and an auto-hiding social chat feed.
- Added camera and world-input locking while radial wheels, maze sketching, or co-op modals are active.
- Added an in-game Co-op Mod Settings menu (accessible from the pause menu and lobby) for keybind rebinding and UI/audio toggles.
- Added external JSON localization synced to Emberward's active language for English, German, Spanish, French, Japanese, Korean, Brazilian Portuguese, Russian, Simplified Chinese, and Traditional Chinese.
