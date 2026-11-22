# Changelog

## 1.0.0

Initial public release.

### Networking & Lobby
- Added Online Steam P2P transport (SteamMatchmaking lobbies, lobby codes, Steam invite overlay, relay support) and Direct LAN transport (TCP reliable channel, UDP unreliable channel with TCP fallback, UDP broadcast auto-discovery).
- Added a dedicated pre-run Co-op Lobby screen launched from the title menu (`CO-OP` button or `F8`), with host-controlled world/stage preview, difficulty tier selector, live-synced rules summary, party roster, ready check, and lobby chat.

### Co-op Gameplay & Rulesets
- Added **Shared Resources** mode (unified gold, energy, deck, and hand synced across all players) and **Separate Resources** mode (independent gold/hands/decks, per-player tower and block ownership with color rings, gold gifting, and independent or host-shared reward drafts and shops).
- Added host-configurable difficulty and pacing rules (enemy HP, wave density, enemy speed, boss HP/armor, starting gold split, wave income multiplier, shared turn block cap, core HP scaling, speed control, wave start policy), built-in presets (*Balanced Co-op*, *Casual*, *Tactical Hardcore*, *Chaos*), and custom JSON preset save/load.
- Added full run, map node, board, tower, tetromino, rune stone, chest, buff, quest, speed, pause, and victory/defeat synchronization with periodic state-hash reconciliation.
- Isolated client runs in a temporary session so solo saves remain untouched while XP, codex records, and achievements commit to the client's profile.
- Added mid-wave disconnect auto-pause modal and post-run contribution breakdown overlay.

### Tactical Tools, Settings & Localization
- Added remote player cursors, translucent ghost previews of hovered/rotated towers and blocks, contextual smart pings and ping wheel, grid-snapped maze path sketching, a 10-sticker emote wheel with 6 quick callouts, build-phase quick undo (`Ctrl+Z`), and teammate HUD hover previews.
- Added an in-game Co-op Mod Settings menu (accessible from the pause menu and lobby) for keybind rebinding and UI/audio toggles.
- Added external JSON localization synced to Emberward's active language for English, German, Spanish, French, Japanese, Korean, Brazilian Portuguese, Russian, Simplified Chinese, and Traditional Chinese.
