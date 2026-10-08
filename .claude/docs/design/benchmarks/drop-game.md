# Benchmark: drop game (chat-driven overlay drop game)

Method: source files read 2026-10-08. The two CodingGarden repos and Instafluff/PenguinDrop were read file by file through `gh api` raw contents (client/app.js, client/config.js, server/src/highScores.js, index.js, config.js, README.md). The PixelPlush settings form is script-rendered, so it was opened in a real browser (Chrome DevTools MCP, take_snapshot of the Parachute Drop setup form). Ours = server/src/NomNomzBot.Infrastructure/Games/Catalog/DropGame.cs, Games/LiveGameEngine.cs, Economy/GameService.cs and Content/Widgets/Assets/drop_game.vue, read this run. Spec: `spec/drop-game.md`.

## Products

- SeedlingDrop (CodingGarden) https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js
- ChristmasDrop (CodingGarden) https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js
- PixelPlush Parachute Drop (setup page) https://www.pixelplush.dev/twitch.html?type=parachute
- PenguinDrop (Instafluff, the original both CodingGarden games adapt) https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md

## Features

| Feature | Seen in | Source | Ours | Status | Slice |
| --- | --- | --- | --- | --- | --- |
| Chat command `!drop` starts a drop | SeedlingDrop, ChristmasDrop, PixelPlush, PenguinDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (startsWith '!drop') | DropGame.cs:32 (InputKeywords) | have | |
| One active drop per user | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (currentUsers) | DropGame.cs:66-68 | have | |
| Custom command name per channel | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (field "Command (e.g. !drop)") | DropGame.cs:32 (keyword fixed in code) | partial | S-DROP-SETTINGS |
| Drop with an emoji argument | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (twemoji.parse of arg) | none | missing | S-DROP-CUSTOM-IMAGE |
| Drop with `me` (viewer avatar) | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (arg === 'me') | none | missing | S-DROP-CUSTOM-IMAGE |
| Drop with a channel emote | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (emote markdown match) | none | missing | S-DROP-CUSTOM-IMAGE |
| Foreign image URL loaded through a third-party proxy | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (duckduckgo proxy URL) | none | not-doing | Reason: a third-party proxy leaks the viewer IP and can vanish; our own server image fetch is in S-DROP-CUSTOM-IMAGE |
| Score by distance from target center (graded 0-100) | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (finalScore) | DropGame.cs:71-73 (distance only, pass or fail by radius) | partial | S-DROP-SCORING |
| Leaderboard of best unique players on screen | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (renderLeaderBoard) | drop_game.vue:125-130 (round results only) | partial | S-DROP-SCORES |
| Persisted high scores per event and platform | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/server/src/highScores.js | none (GamePlay rows hold payout, no landing score) | missing | S-DROP-SCORES |
| Score chat commands (top, recent, high, low) | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (instructions text) | none | missing | S-DROP-SCORES |
| Mod reset command | PixelPlush, PenguinDrop | https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md (resetdrop) | LiveGameEngine.cs (cancel by REST only, no chat command) | partial | S-DROP-MODES |
| Queue drops, then start all at once (group drop) | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (!queuedrop, !startdrop) | DropGame.cs:36 (60 s lobby window, one mode) | partial | S-DROP-MODES |
| Group drop only switch | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (GROUP DROP ONLY) | none | missing | S-DROP-MODES |
| Raid drop timer (a raid triggers a drop) | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (RAID DROP TIMER) | none | missing | S-DROP-MODES |
| Owner clears all landed items by chat | ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (!clear) | none | missing | S-DROP-MODES |
| Channel point redemption starts a drop | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (CHANNEL POINT COST) | none | missing | S-DROP-REDEEM |
| Channel point and bit cheer variants | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (CHANNEL POINT CHEERS, BIT CHEERS) | none | missing | S-DROP-REDEEM |
| Ready cooldown between drops | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (TIME TIL READY) | none (one drop per round only) | missing | S-DROP-REDEEM |
| Overlay hides until a drop happens | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (HIDE UNTIL DROP) | drop_game.vue:105 (v-if visible, set by round_open) | have | |
| Auto-hide / refresh timer in seconds | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (REFRESH TIMER 90) | drop_game.vue:16,78-81 (hideAfterMs) | have | |
| Target position setting, or a random target | PixelPlush, PenguinDrop | https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md (randomTarget) | DropGame.cs:43 (random only, no fixed option) | partial | S-DROP-SETTINGS |
| Theme choice (several visual sets) | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (CHOOSE THEME, 13 options) | none (one fixed track look) | missing | S-DROP-THEMEPACK |
| Background and clouds toggles | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (BACKGROUND, CLOUDS) | none | missing | S-DROP-THEMEPACK |
| Landed item changes look by score (seedling grows) | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (addSeedling) | drop_game.vue:173-182 (dot, green when hit) | partial | S-DROP-THEMEPACK |
| Chute animation follows direction and speed | PenguinDrop | https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md (newChute) | none | missing | S-DROP-THEMEPACK |
| Eagle drops a viewer into a nest (owner theme idea) | owner request 2026-10-07 | plan OWNER REQUEST drop game | none | missing | S-DROP-PACK-EAGLE |
| Random horizontal and vertical velocity per drop | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (doDrop velocity) | none (landing is a number, no path) | missing | S-DROP-PHYSICS |
| Wind: speed changes back and forth in flight | PenguinDrop | https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md (windyFloat) | none | missing | S-DROP-PHYSICS |
| Drop-to-drop collisions in the air | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (elasticCollisionWith) | none | missing | S-DROP-PHYSICS |
| Landed items fall off when a new drop hits them | ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (dropCollidedOrnaments) | none | missing | S-DROP-PERSIST |
| Landed items survive an overlay reload | ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (localStorage.ornaments) | LiveGameEngine.cs (session StateJson, not replayed to a new overlay) | partial | S-DROP-RELOAD |
| Sound effects volume and notification volume | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (SOUND VOLUME, NOTIFICATION VOLUME) | none | missing | S-DROP-AUDIO |
| Chat announcement with a format string | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (CHAT ANNOUNCEMENT MESSAGE FORMAT) | none | missing | S-DROP-ANNOUNCE |
| Result decided away from the browser (seeded, replayable) | none; every product rolls Math.random in the browser | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (Math.random in doDrop) | DropGame.cs:71 (server roll, no seed or path sent) | partial | S-DROP-SEED |
| Overlay scales with the browser source size | SeedlingDrop, ChristmasDrop | https://github.com/CodingGarden/ChristmasDrop/blob/HEAD/index.js (onWorldResize) | drop_game.vue:135-146 (min(720px, 90vw), fixed 46px track) | partial | S-DROP-RESPONSIVE |
| Luck boost for a held item | owner request 2026-10-07 | plan OWNER REQUEST drop game | none | missing | S-DROP-LUCK |
| Browser source URL with the channel in it | PenguinDrop | https://github.com/Instafluff/PenguinDrop/blob/HEAD/README.md (Instructions) | widget overlay URL (widgets-overlays.md row 12) | have | |
| Built-in debug spawner | SeedlingDrop | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (DEBUG testDrop) | none | not-doing | Reason: the widget editor test runner fires events on demand; no extra switch in the overlay |
| Paid premium themes and a goodies market | PixelPlush | https://www.pixelplush.dev/twitch.html?type=parachute (Premium Add-On) | none | not-doing | Reason: no paid theme market; packs come from the widget gallery, self-hosting stays free |
| Currency payout on landing | ours only; none of the four pay currency | https://github.com/CodingGarden/SeedlingDrop/blob/HEAD/client/app.js (no economy code) | DropGame.cs:92-143 (payout x stake inside the win radius) | have | |
