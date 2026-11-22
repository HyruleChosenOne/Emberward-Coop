# Changelog

## 1.0.0

Initial public release.

### Networking, Lobby & Save System
- Added Online Steam P2P transport (SteamMatchmaking lobbies, lobby codes, Steam invite overlay, relay support) and Direct LAN transport (TCP reliable channel, UDP unreliable channel on `port + 1` with TCP fallback, UDP broadcast auto-discovery).
- Added strict Steam matchmaking, friend-lobby AppID, and LAN discovery validation so only valid Emberward Co-op lobbies appear in the browser, with version mismatch badges when versions differ.
- Added a Pre-Run Co-op Lobby launched from the title menu (`CO-OP` button or `F8`), with game mode selector (Campaign, Endless Mode, Enigma Sanctum), world and Endless stage previews, difficulty selector, live-synced rules summary, party roster with inline latency readouts, Lobby Hero Avatar picker, ready check, and lobby chat.
- Added isolated co-op sessions for both host and clients so single-player active-run save slots are never loaded or overwritten, paired with a dedicated `CoopSaves/` save/load system (`NEW CO-OP RUN` / `LOAD CO-OP RUN`, automatic mid-run saving, and manual pause-menu saving).
- Added party-best mode, world, and difficulty unlock resolution from the most-progressed player in the lobby, plus a memory-only **Unlock All Maps/Modes (Co-op Only)** toggle in Mod Settings that never touches solo savefiles.

### Co-op Gameplay, Voting & Drafting
- Added **Shared Resources** mode (unified gold, energy, core health, towers, cards, and relics) and **Separate Resources** mode (independent gold, hands, decks, and relics, per-player tower and block ownership with color rings, read-only inspection on teammate towers, and mid-run gold gifting).
- Kept **Core Health** unified as a single shared pool across the party in both modes.
- Integrated vanilla in-game Home hero/ember selection (host-selected and synced live to clients) and vanilla Academy, reward, shop, chest, altar, and campsite popups.
- Added map-node voting (floating vote chips, consensus resolution, host tie-break/resolve), live shared draft voting and reroll requests in Shared Resources mode, and simultaneous deterministic per-player drafts/shops with a completion barrier in Separate Resources mode.
- Added host-configurable difficulty and pacing rules (enemy HP, wave density, enemy speed, boss HP/armor, starting gold split, wave income multiplier, shared turn block cap, shared core HP bonus, gifting cap, draft sync mode, speed control, auto-scale), built-in presets (*Balanced Co-op*, *Casual*, *Tactical Hardcore*, *Chaos*), and custom JSON preset save/load.
- Added run, map node, board, tower, tetromino, rune stone, chest, buff, quest, speed, pause, and victory/defeat synchronization with periodic state-hash checks.
- Added solo profile meta-progression commits for both host and clients (XP, stage/world clears, boss clear flags, tower/relic codex entries, and achievements) without touching solo active-run saves.
- Added mid-wave disconnect auto-pause modal and post-run contribution breakdown overlay.

### UI, Tactical Tools, Settings & Localization
- Built mod windows, HUD elements, hero portraits, stickers, and ping decals using Emberward's in-game UI sprites, frames, icons, fonts, and audio.
- Applied player colors strictly to player names across the lobby, HUD, hover tooltips, nameplates, and post-run summary while keeping character portraits untinted, with a color stepper in both the lobby footer and Mod Settings.
- Added remote player cursors, translucent ghost previews of hovered/rotated towers and blocks, contextual smart pings and a 5-slice ping wheel (`Middle Mouse`, `F1`–`F5`), grid-snapped maze path sketching (`Left Alt + Left Drag`), a 10-sticker emote wheel with 6 quick callouts (`T`, `Tab`/scroll to switch tabs, `1`–`6` direct send), a collapsible Players HUD (`F7`) with inline latency and hover previews, and a filtered auto-hiding chat feed.
- Added camera and world-input locking while radial wheels, maze sketching, or co-op modals are active, with clean unlock on `Escape` or focus loss.
- Added in-game Co-op Mod Settings (`Pause -> CO-OP SETTINGS` and Lobby `Mod Settings`) for player color, co-op-only unlocks, keybind rebinding, and UI/audio preferences.
- Added external JSON localization synced to Emberward's active language for English, German, Spanish, French, Japanese, Korean, Brazilian Portuguese, Russian, Simplified Chinese, and Traditional Chinese.
