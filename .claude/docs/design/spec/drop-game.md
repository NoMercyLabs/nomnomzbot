# Spec: Drop Game (generic logic, theme packs, server-seeded landing)

**Status:** decided 2026-10-08. Builds on `live-games.md` (engine, `ILiveGame`, `GameSession`) and `widgets-overlays.md` (row 12, `drop_game`). Replaces plan item S082. Benchmark: `benchmarks/drop-game.md` (every slice below comes from a row there).
**Owner words (2026-10-07):** "i like the drop game to be interchangable with other assets or versions, so make the logic generic and configurable and make the assets responsive and dynamically changable. for example i like a birds nest with an eagle that drops 'meat' (the user) to the nest... this is not now but needs to be prepared for" / "give the lucky feather holder more drop luck" / "look at PixelPush for their config options to see what we can onboard" / "do we calculate the random path with a seed or something? how do you intend to do this server side and how much would this cost saas?"

Items marked "default, owner may change" are open in `questions.md`; the default ships until the owner answers.

## 1. Split: game logic, theme pack, overlay

| Part | Owns | Never owns |
| --- | --- | --- |
| `DropGame` (`ILiveGame`, server) | spawn, seed, landing, score, luck, modes, payout | any pixel, image name, sound or colour |
| Theme pack (data) | art, sounds, anchors, motion style, labels | any rule, odds or payout |
| Overlay widget `drop_game` | animate a path from a seed, draw the pack, scale to the stage | deciding where a drop lands |

The logic reads only a `DropRules` record from `GameConfig.ConfigJson`; the pack is chosen by id and never read by the server except for its manifest `kind` and `stage` size. A new pack therefore needs no server edit and no new `ILiveGame`.

## 2. Server-seeded landing (answers: seed? server side? cost?)

Decision: yes, a seed. The server decides the result. The client only animates.

Per drop, in `DropGame.OnInputAsync`:

1. Draw a 64-bit `seed` from the CSPRNG (`IGameRandomizer`, new member `NextUInt64`). Production stays CSPRNG; tests use a fixed fake, so the same input gives the same seed.
2. Run a seeded generator (splitmix64 to seed xoshiro256**, integer math only) over the seed to get: spawn x, sway phases, wind gusts, and the landing offset `u` in [-1, 1].
3. Apply luck (section 6) to `u`, then `landingX = target + u * spread`. Score = `100 * (1 - |landingX - target| / halfWidth)` clamped to 0..100 (S-DROP-SCORING).
4. Send one frame `{kind:"drop", dropId, seed, spawnX, landingX, score, hit, startedAt, flightMs, user, image, boosted}` to the overlay.

The client builds its path from `seed` (cosmetic sway and wind only) and is forced through `spawnX` at t=0 and `landingX` at t=flightMs: path(t) = lerp(spawnX, landingX, ease(t)) + sway(seed, t) * envelope(t), with envelope(0) = envelope(1) = 0. A float difference between C# and TypeScript can only change the sway, never the landing. The client never reports a result back. Chat and payout use the server number.

Replay: the persisted row holds `seed`, config snapshot id and `landingX`. A test re-runs the generator from the seed and must get the same `landingX` (this is the audit and bug-report tool).

Cost per drop (design estimate, not measured; S-DROP-SEED adds a benchmark test that measures it):

| Item | Value | How derived |
| --- | --- | --- |
| In-flight state in memory | about 200 bytes | seed 8 B, user id 16 B, name about 16 B, image ref about 40 B, times 16 B, landing 4 B, plus object overhead |
| Wire frame | about 250 bytes JSON | one frame per drop, one per round result |
| CPU on the server | about 20 microseconds | one PRNG init and about 8 draws (under 2 us), JSON write about 5 us, hub send about 10 us |
| Persisted row (`GamePlay`) | about 150 bytes | seed, landing, score, stake, payout |
| Server physics loop | none | the server does not simulate frames |

At 100 channels x 50 drops per hour = 5,000 drops/hour = 1.4 drops/second:
- CPU: 1.4 x 20 us = about 28 us per second, which is 0.003% of one core.
- Memory: a flight lasts about 8 s, so about 11 drops in flight = about 2 KB for all channels.
- Egress: 5,000 x 250 B = 1.25 MB/hour = 30 MB/day = about 0.9 GB/month.
- Storage: 5,000 x 150 B = 750 KB/hour = 18 MB/day = 6.6 GB/year; keep detail rows 90 days (about 1.6 GB), then keep only a per-player best score.
- Money: the increment is below the noise of the base host. The drop game needs no extra server, no tick loop and no extra service. The cost grows in a straight line with drops; 10x the drops is 10x of the numbers above, still not a sizing factor.
- Rejected: simulating physics on the server at 60 Hz. It costs a tick loop per channel and gives no fairness gain, because the outcome is already server-decided.

SaaS note: SaaS operation of NomNomzBot is reserved to NoMercy Labs. Self-hosting is always free. The numbers above apply equally to a self-hosted box.

## 3. Game rules (all configurable, `DropRules`)

Stored in `GameConfig.ConfigJson` (`drop_game` row, `GameService.cs:59`). All keys have in-code defaults and are validated on save; none is baked into the pack.

| Key | Meaning | Default | In PixelPlush |
| --- | --- | --- | --- |
| `mode` | single, group, queue, raid | single | yes (see section 5) |
| `spread` | half width of the landing range, in stage units | 960 | no |
| `target_x` | fixed target position, or `random` | random | yes |
| `win_radius` | stage units counted as a hit | 10 percent of spread | no (score only) |
| `flight_ms` | drop time from spawn to land | 8000 | no |
| `payout_multiplier` | existing column | 2 | no |
| `luck` | section 6 | see there | no |
| `physics` | wind amount, sway amount, collisions on/off | wind 0.4, sway 0.5, collisions off | partly (PenguinDrop windyFloat) |

## 4. Theme packs

A pack is a folder (or gallery bundle) with `pack.json` plus assets. It is a widget-gallery item of kind `drop-pack` (first-party packs ship in the repo; community packs go through gallery review like any widget).

`pack.json`:
- `id`, `name`, `version`, `kind: "drop"`, `author`, `license`, `credits[]`.
- `stage`: reference stage `{w:1920, h:1080}`. All coordinates are in stage units.
- `motion`: `carrier` = `attached` (parachute on the item), `flyover` (a carrier crosses and releases), or `none` (item falls bare); `releaseAt` for flyover; `sway`, `windSensitivity`.
- `slots` (each: `src` svg or png @2x, `anchor`, `size`, optional `z`):
  - `background`, `foreground` (optional layers; PixelPlush BACKGROUND and CLOUDS map here)
  - `carrier` (parachute, eagle)
  - `payload` (frame the viewer image sits in: a plain circle, a piece of meat)
  - `target` (grass target, nest) with `hitbox` = the scoring band
  - `landed` (what remains: seedling that grows with score, a catch in the nest) with `growWithScore`
  - `effects[]` (splash, feathers) and `sounds{ spawn, land, hit, miss }`
- `labels`: name tag style, font, colour tokens.

Shipped packs:
1. `parachute` (default): carrier `attached` parachute, payload = viewer image, target = grass band, landed = seedling that grows with score. This is the SeedlingDrop and PixelPlush Parachute shape.
2. `eagle-nest` (prepared by S-DROP-PACK-EAGLE): carrier `flyover` = an eagle that enters from the side, flies to the server's `spawnX` and releases; payload = meat shape holding the viewer image; target = nest at `target_x`; landed = meat in the nest, a miss = meat falls past with a small splash or thud. Same rules, same frames, different `pack.json` and art. No server code differs.

Swap: the channel setting `pack_id` changes in the dashboard. The server pushes `pack.changed {packId, manifestUrl}` over the existing widget event channel. The overlay preloads the new manifest and assets in the background and swaps at the next idle moment (no drop in flight); no reload and no new browser-source URL. A pack that fails to load keeps the old one and reports the error to the dashboard inbox. Pack assets are served through the widget asset route; an uninstalled pack falls back to `parachute`.

## 5. Responsive assets and play modes

Responsive: the overlay draws into the stage (1920x1080 units) and scales by `min(width/1920, height/1080)` (fit) or `max(...)` (fill, a pack setting). A `ResizeObserver` rescales live; there is no pixel constant in the widget code. Text uses stage units. SVG is preferred; PNG must be @2x. Check: a 1280x720 and a 3840x2160 browser source show the same layout.

Modes (`mode`; default single, owner may change):
- `single`: each valid `!drop` flies at once; one live drop per viewer; ready cooldown `cooldown_s` (default 0).
- `group`: a lobby window (`lobby_s`, default 60, existing manifest value) collects drops; all fly together on start.
- `queue`: mods use `!queuedrop` to collect and `!startdrop` to release (PixelPlush parity); `!resetdrop` cancels and refunds.
- `raid`: a raid starts a group round for `raid_window_s` (default 30).
- Triggers: chat command (`command`, default `!drop`), channel-point redemption (`redeem_cost`, group variant `redeem_group_cost`), bit cheer (`cheer_min_bits`). Drop arguments: emoji, `me` (avatar), channel emote; the server fetches and caches the image itself (viewer IPs never reach a third party).

## 6. Luck boost (Lucky Feather holder)

The boost is channel data, never a number in code.

- Setting `luck.holder_boost` (0 to 1, default 0.25, owner may change) and `luck.holder_source` (the channel record type naming the holder, default `lucky_feather`; the same lookup the Lucky Feather pipelines already use).
- Rule: at drop time the engine asks the holder lookup whether the dropping viewer holds the item. If so, the seeded offset is pulled toward the target: `u' = u * (1 - boost)`. The seed and RNG stream are unchanged, so replay stays exact (the replay test reads the persisted `boostApplied`).
- The frame carries `boosted:true`; the overlay shows a feather on the payload so the effect is visible to everyone (rule: consequences are visible).
- Any other buff source (sub tier, VIP) is a second `luck.sources[]` entry with its own boost; the stack is capped by `luck.max_boost` (default 0.6).

## 7. State after an overlay reload (seed provider)

The overlay keeps no authority. On connect (and on every reconnect) it calls hub method `GetDropState`. The server answers from the live session snapshot (`GameSession.StateJson`, `live-games.md` D9):
`{serverNow, packId, target, rules, flying:[{dropId, seed, spawnX, landingX, startedAt, flightMs, user, image, boosted}], landed:[{dropId, user, image, landingX, score}], leaderboard:[...]}`.
The client sets its clock offset from `serverNow`, recomputes each flying path from its `seed` at `t = serverNow - startedAt` (so the animation resumes mid-air at the right place), and redraws `landed`. Landed items persist for `landed_ttl_s` (default 90, PixelPlush refresh timer) or until `!cleardrop`. The seed is the state: nothing else has to be stored in the browser.

## 8. Settings mapped from PixelPlush

Source: Parachute Drop setup form, https://www.pixelplush.dev/twitch.html?type=parachute (read 2026-10-08).

| PixelPlush name | Meaning | PixelPlush default | Ours (key) |
| --- | --- | --- | --- |
| Twitch channel name | which channel the overlay serves | empty | yes (implicit: the widget is per channel) |
| Choose game | which game | Parachute Drop | yes (`drop_game` is one game; others are other `ILiveGame`s) |
| Choose theme | visual set | first in list | yes (`pack_id`) |
| Background | draw the backdrop | on | yes (pack slot `background`, `show_background`) |
| Clouds | draw the clouds | on | yes (pack slot `foreground`, `show_foreground`) |
| Hide until drop | overlay invisible when idle | off | yes (`hide_until_drop`, default on, matches drop_game.vue) |
| Score in chat | post the score to chat | off | yes (`announce_enabled`) |
| Chat announcement message format | text with USERNAME and POINTS | "USERNAME landed for POINTS!" | yes (`announce_template`, ours uses template variables, never a bare token) |
| Bit cheers | bit cheer starts a drop | off | yes (`cheer_min_bits`) |
| Enable drop via chat | allow the chat command | on | yes (`chat_enabled`) |
| Chat command | the command word | !drop | yes (`command`) |
| Sound volume | effects volume 0-100 | 25 | yes (`sound_volume`) |
| Notification volume | alert volume 0-100 | 25 | yes (`notification_volume`) |
| Channel point cost | redemption price | 0 (off) | yes (`redeem_cost`) |
| Channel point cheers | redemption cost, cheer variant | 0 | yes (`redeem_cheer_cost`) |
| Channel point group drop | redemption cost, group | 0 | yes (`redeem_group_cost`) |
| Refresh timer (secs) | landed items stay this long | 90 | yes (`landed_ttl_s`) |
| Time til ready (secs) | cooldown between drops | 0 | yes (`cooldown_s`) |
| Raid drop timer (secs) | raid starts a drop | 0 (off) | yes (`raid_window_s`) |
| Group drop only | no single drops | off | yes (`mode = group`) |
| Set target pos (screen %) | fixed target position | empty (random) | yes (`target_x`) |
| Character picker (cake icons) | choose the dropped item | first | partly (`default_image`; per drop the viewer picks with arguments) |
| Premium add-on and bundle price | paid themes | n/a | no (paid theme market is not-doing, benchmark row) |

All of these live in one dashboard settings form built from the generic settings schema (`WidgetSettingsSchemaProvider`), saved to the `drop_game` `GameConfig`. Wording of labels comes from locale keys, not from code.

## 9. Testing rules for the slices

- Seed: same seed and rules give the same `landingX` in C# (unit) and in the overlay's TypeScript (E2E, BigInt integer path), and the client path ends within 0.5 stage units of `landingX`.
- Luck: with boost 0.25, over 10,000 seeded drops the mean distance of a holder is lower than a non-holder by the expected factor; boost 0 gives identical results to no holder.
- Pack swap: an E2E swaps `parachute` to `eagle-nest` with a drop in flight; the flight completes in the old pack, the next drop uses the new one, no reload happens.
- Reload: an E2E reloads the overlay mid-flight and the drop resumes at the computed position.
