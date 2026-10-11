# UI restructure: research paper and one plan (2026-10-11)

Owner request, verbatim (Stoney, 2026-10-11): "cool, now that this research is done. i like you to do the same full deep research on restucturing and reorganizing the user interface from the ground up. all the features in the bot need to have a proper place and it must be prepared for so it can be executed in one shot without errors or contstant shuffling. i want all features to have a documented place in proper hirarchy, importance and organization by who uses it and so its ensured that everything is right in front of you when you need it the most or when you want to create some shenanigans on stream with all of its features. every viewer, mod and streamer differences all layed out. and then finally i want a verdict if kotlin multi platform still the right language and framework is for the goal of serving hundred thousands of users at once. do not leave a stone unturned and store everything you find along the way and give me a full report and plan at the end"

How to read this paper:

- Short sentences. One idea per line.
- "PLAN" means `.claude/docs/design/SHORTCOMINGS-EXECUTION-PLAN.md`. This paper's slices are in its section "OWNER REQUEST 2026-10-11".
- "Lane Lx" means a research note in this session's scratchpad (`ui/Lx-*.md`). Every fact this paper needs is copied here, with the file and line the lane read.
- The benchmark is `benchmarks/dashboard-ia.md`. The full per-feature placement is `ui-placement-ledger-2026-10-11.md`.
- Anything not read or not run is in the coverage ledger (section 14) as blocked, with the reason.

## 0. The answer in short

- Today the dashboard has 45 sidebar pages in 9 groups, plus the Admin page. They are grouped by the kind of thing (chat, music, stream), not by the job the user does.
- The new layout groups pages by job: Home, Live, Build, People, Loyalty, Music & Voice, Connect, Setup. Admin moves behind the profile menu.
- "Build" puts commands, event responses, triggers, pipelines, scripts and overlays in one place. This is the owner's 2026-09-08 navigation ask.
- A new page, "Deck", is the shenanigans board: one tap per action, live on stream, on the phone, in a pop-out window and on a Stream Deck.
- One shell for every role. A role sees a subset. Nothing forks per role.
- The broadcaster sees 34 sidebar rows instead of 45. 8 pins per role keep the daily pages on top.
- The plan runs in one order: 1 spec slice, 2 base slices, 10 parallel lanes, 6 finishing slices, and a guard script that fails when a page has no place.
- KMP verdict: keep Kotlin Multiplatform and Compose for the streamer, moderator and editor dashboard. Do not use Compose for the high-traffic public viewer pages. The limit for 100,000 users at once is the server (no SignalR backplane), not the language. Fix the 46 MB first load now.

## 1. What we have today (measured, not assumed)

### 1.1 Pages and groups

Source: `app/.../shell/nav/ShellNav.kt:113-202` (Lane L1, script-built census, 110 of 110 surfaces located).

| Group today | Pages | Read floor | Manage floor |
|---|---|---|---|
| Home | Dashboard | Moderator | read-only |
| Chat (12) | Chat, Multi-Chat, Commands, Chat Triggers, Voice Triggers, Timers, Event Responses, Pipelines, Code Scripts, Widgets, Pick Lists, Quotes | Moderator (Code Scripts: Broadcaster) | Moderator (Chat, Commands), Editor (rest), Broadcaster (Code Scripts) |
| Moderation (4) | Moderation, Queue, History, Rules: 4 routes, 1 screen | Moderator | Moderator (Rules: Editor) |
| Loyalty (4) | Rewards, Economy, Games, Giveaways | Moderator | Editor (Giveaways: Moderator) |
| Music (6) | Music, Song Requests, TTS, Sound Clips, Assets, Media Share | Moderator | Editor (Media Share: Moderator) |
| Stream (3) | Alerts, Analytics, Schedule | Moderator | Editor |
| Community (1) | Community | Moderator | Moderator |
| Connect (8) | OBS, VTube Studio, Discord, Webhooks, Custom Events, Supporters, Federation, Automation | Moderator to Broadcaster | Super Mod (Discord), Editor, Broadcaster |
| Setup (6, collapsed) | Bundles, Integrations, Roles, Features, Settings, My Data | Moderator to Broadcaster | Editor, Broadcaster |
| Admin (isAdmin) | 23 tabs (`AdminScreen.kt:281`) | platform admin | platform admin |

Other surfaces:

- Viewer shell: 6 participant pages (My channel, Now playing, Leaderboards, Points and store, Games, Me) at `ParticipantNav.kt:115-133`.
- Sign-in, setup wizard, splash and "unreachable" screens: 5 destinations (`Destination.kt:21-33`).
- Server-served pages: the OBS overlay host (`OverlayHostController.cs:35`) and the HTML landing page at `/` (`Program.cs:965-970`).
- Size: 1,124 composables, 22 keyed tab rows, 202 dialogs and sheets, 609 buttons.
- The deepest path is Admin: sidebar, Admin, tab group, tab (23), then a dialog (35 dialogs).

### 1.2 Server features without a screen

Source: Lane L2, script census over `server/openapi/v1.json` and the app source.

- 882 REST operations on 709 paths.
- 702 have an app call and a screen.
- 30 have an app client method, but no screen calls it.
- 150 have no app call at all. The largest gaps: Automation data plane 0 of 18, Auth 8 of 27, Admin 74 of 93, Discord 17 of 26, Egress allowlist 0 of 5, Channels 12 of 18.
- The placement ledger gives each of these 180 operations a place, or a reason why it has no screen (for example: a device API, an OBS page, a webhook receiver).

### 1.3 What is wrong with the layout today

Each item was read in the code or the specs (Lanes L1, L3).

- Grouped by thing, not by job. Commands, overlays (Widgets) and pipelines are in "Chat". Alerts are in "Stream". Sounds are in "Music". To make one new command with a sound and an overlay, the user visits 3 groups. This is the exact pain in the owner's 2026-09-08 sentence (section 2).
- Duplicates:
  - Sound Clips and Assets: two pages with the same read key `sounds:read` (`ShellNav.kt:161`, `:164`).
  - Alerts and Event Responses both answer stream events.
  - Twitch app credentials in 3 places: Integrations, the setup wizard and Settings.
  - Ko-fi on two pages: Webhooks (`WebhooksScreen.kt:801`) and Supporters (`SupportersScreen.kt:124`).
  - Music and Song Requests: two entries for one music flow (`MusicScreen.kt:224` jumps to Song Requests).
  - Language choice is in 3 places: onboarding, the streamer profile menu and the viewer profile menu. This one is correct: onboarding is the first-run step, and both menus are the same control.
- Wrong words:
  - The UI says "Super Mod". The owner settled "Lead Moderator" on 2026-06-23 (section 2).
  - "Rewards" means Twitch Channel Points. "Pick Lists" means random responses. "Widgets" are overlays.
- Wrong places:
  - My Data is in Setup with a Moderator read floor. Every viewer owns their data, so the floor is Everyone and the place is the profile menu.
  - The OBS page holds the OBS connection form. Connections live in Integrations (settled rule: feature pages carry no wire-up controls).
  - Roles is in Setup. It is about people, so it belongs with People.
  - Code Scripts is a list page in front of the editor. The owner said on 2026-09-08 that this page should not exist; the editor is the page.
  - Setup starts collapsed (`ShellScreen.kt:893`), so Integrations and Roles are 2 clicks away.
- Not live everywhere: 22 of 70 app controllers listen for live changes (`ConfigChangeRefresh.kt:21-33`; moderation paper 2026-10-10, row 40). Community, Roles, Alerts, Event Responses, Integrations, the TTS queue and Settings do not. The owner asked on 2026-10-10 that "EVERYTHING must update live and fast".

## 2. What the owner already decided (verbatim, on disk)

These sentences are binding inputs. The new layout must satisfy each one.

- 2026-09-08T17:19:53Z: "there should be a beter organization in navigating, in the sense that all stream features and behaviors are in one place, administative features for the bots behavior from a users perspecitve close together and so on. i currently feel that there is too much juggeling required to find features related to getitng a new command working or adding a custom overlay with special actions and so on."
- 2026-09-08: "chat also needs a shield mode toggle for direct access."
- 2026-09-08: "the tts page needs to have tabs because there is too much on one page, properly split by type."
- 2026-09-08: "the main code script edit page should not exist, this should directly be the code editor with the testing helpers inside the editor..."
- 2026-09-08: "the ui should have a font for each language..."
- 2026-10-10: "EVERYTHING must update live and fast, that includes all other pages on the dashboard, no delayed states".
- 2026-10-10 (slice S-USER-HUB): community and moderation become one tabbed interface.
- 2026-06-23: "Use the same terminology as twitch does please so lead moderator and not super moderator."
- 2026-06-16, role order: "viewer -> sub -> vip -> mod -> super mod -> channel editor -> broadcaster, artist and staf are real roles too".
- 2026-10-11, clearance for S-IA-SPEC: "if you are sure that this matches human psychology for dashboards then you have my clearance to execute".
  Arc's reading: the layout passed the check in `ui-page-drawings-2026-10-11.md` section 10, after three
  fixes to Home Live. The plan runs; nothing is pushed until the owner is happy with how it runs.
- 2026-10-11 (answer to the Commands floor question in section 11): "moderators get granted the permission to manage stream and channel behaviors".
  Arc's reading: every page that shapes how the stream and the channel behave has a Moderator manage floor: Live (Stream, OBS, VTube Studio, editing the Deck), Build (all of it), Loyalty, and Music & Voice (the TTS Provider tab stays Editor). Setup, Connect, People: Rules and role assignment keep their floors. A broadcaster can still raise any action for their channel (ChannelActionOverride, ActionDefinitionSeeder.cs:131-132).
- 2026-10-11 (answer to the risk raised on four actions): "we will leave the risky items to the channel manager and up".
  Arc's reading: the channel manager is the Editor role. Content labels and branded content (legal statements in the streamer's name), ads and raids stay at Editor.
- 2026-10-11: "and we are V0 so no backwards compattible bullshit tangled in the code".
  Arc's reading: the restructure replaces the old routes outright. It has no alias map, no redirect of the previous page links and no mapping of saved pins. Scripts/check-no-compat.py blocks such code at commit.

Settled rules from `spec/frontend-ia.md` and `PRODUCT-ALIGNMENT.md` that this paper keeps (Lane L3):

- One shell, gated by role. Never a separate app per role.
- Hide a page when the user is below its read floor. Disable an action, with the reason, when the user is below its manage floor.
- Feature pages carry no connection (wire-up) controls. Integrations owns connections.
- The profile menu holds identity, theme, language and the switch into Admin.
- Stream live operations are on Home.
- Persona order: streamer first, then a moderator of many channels, then the viewer.
- Users see role names, never numbers.
- A consequence is shown before saving (blast radius).

## 3. The new layout

### 3.1 The rule that places every feature

Each feature goes to the group of the job the user is doing when they need it:

- **Live**: things you use while the stream runs, with chat open.
- **Build**: things you make once, and the bot then runs by itself.
- **People**: everything about a person in your chat: who they are, what they did, what they may do.
- **Loyalty**: what viewers earn and spend.
- **Music & Voice**: what plays and what speaks.
- **Connect**: other services that send things in or receive things out.
- **Setup**: things you set once for the whole channel.

A tie goes to the job that happens most often. Example: the OBS scene switch is used live on every stream, so OBS control is in Live. The OBS connection is set once, so it is in Setup, Integrations.

### 3.2 Sidebar groups, pages and tabs

Floors: "Read" is the lowest role that sees the page. "Manage" is the lowest role that changes it. The names are the user-visible role names.

**Home** (no group header)

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Home | Streamer: Prep (offline) and Live (on air). Moderator: Mod home. Viewer: My channels | Everyone | per card | One page, shaped by role and by stream state. Live layout: stream health in the header (a red card only on a problem), attention inbox top left, top 8 Deck buttons, activity, chat as a full-height column. Prep layout: the Stream info editor (the same component as Live, Stream), the schedule, the checklist. |

**Live**

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Chat | One channel; All my channels (was Multi-Chat) | Moderator | Moderator | Shield mode toggle in the header (owner 2026-09-08). Platform chips filter Twitch, Kick, YouTube, X. |
| Deck | Grid; Edit | Moderator | Moderator (each button has its own floor) | NEW. Section 4. |
| Stream | Info & presets; Interactions; Schedule | Moderator | Moderator (content labels, branded content, ads, raids: Editor) | Info & presets: title, category, tags, per platform, with "All platforms". Interactions: polls, predictions, raids, ads, markers, clips, goals, hype train, charity, Guest Star. Schedule: was its own page. |
| OBS | Live control; Advanced | Moderator | Moderator | Scenes, sources, recording, replay. The connection moves to Integrations. Without a connection, the page shows the connect step inline. |
| VTube Studio | Hotkeys; Expressions | Moderator | Moderator | The same inline-connect pattern. |
| Analytics | Stream; Chat; Growth | Moderator | read-only | |

**Build** (one place to make a command, an overlay or an action: the owner's 2026-09-08 ask)

| Page | Tabs or modes | Read | Manage | Notes |
|---|---|---|---|---|
| Commands | Custom; Built-in | Moderator | Moderator | Built-in: the 36 built-in commands, each with its own floor. |
| Event Responses | by event type | Moderator | Moderator | Absorbs Alerts. One page answers every stream event (follow, sub, raid, cheer, reward, donation). |
| Triggers & Timers | Chat triggers; Voice triggers; Timers | Moderator | Moderator | Three small pages become three tabs. |
| Pipelines | List; Editor | Moderator | Moderator | |
| Scripts | the editor itself | Moderator | Moderator | Opens straight into the code editor, with the file list and the test helpers inside it (owner 2026-09-08). The server already lets a Moderator write scripts (ActionDefinitionSeeder.cs:174-179). |
| Overlays | Gallery; My overlays | Moderator | Moderator | Was "Widgets". The 23 widget kinds. Each overlay shows its OBS URL and a test button. |
| Library | Sounds; Files; Random responses; Quotes | Moderator | Moderator | Sounds was Sound Clips. Files was Assets. Random responses was Pick Lists. Every picker in Build can upload inline. |
| Gallery | Templates; Bundles | Moderator | Moderator | Bundles moves here from Setup. Every create flow also has "Start from a template". |

**People** (S-USER-HUB: one screen; the sidebar children are its tabs)

| Tab | Read | Manage | Notes |
|---|---|---|---|
| Overview | Moderator | - | Standing of the chat, actions needed, active holds, mass-ban batches. |
| Queue | Moderator | Moderator | One queue for held messages, reports, appeals and unban requests. |
| Viewers | Moderator | Moderator | Was Community. The viewer card: identity on every linked platform, history, notes, points, roles, per-user settings (owner 2026-09-08). |
| History | Moderator | read-only | Every moderation action: by whom, on which platform. |
| Rules | Moderator | Lead Moderator | AutoMod, blocked terms, spam defense, mass-ban settings and opt-in. |
| Roles | Moderator | Broadcaster (assign Moderator and VIP: Lead Moderator) | Moved from Setup. Includes "Individual permissions" (per-user overrides). |

**Loyalty**

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| Channel Points | Rewards; Redemptions | Moderator | Moderator | Was "Rewards". |
| Economy | Currency; Store; Savings jars; Leaderboards | Moderator | Moderator | |
| Games | Games; Live sessions | Moderator | Moderator | Game starts are also Deck buttons. |
| Giveaways | Active; History | Moderator | Moderator | |

**Music & Voice**

| Page | Tabs | Read | Manage | Notes |
|---|---|---|---|---|
| Music | Now playing; Song requests; Settings | Moderator | Moderator | Song Requests becomes a tab. |
| TTS | Live; Voices; Rules; Provider | Moderator | Moderator (Provider tab: Editor) | Owner 2026-09-08: tabs split by type. |
| Media Share | Queue; Settings | Moderator | Moderator | |

**Connect**

| Page | Read | Manage | Notes |
|---|---|---|---|
| Discord | Moderator | Lead Moderator | |
| Webhooks | Broadcaster | Broadcaster | Generic webhooks only. Ko-fi moves to Supporters. |
| Custom Events | Moderator | Editor | |
| Supporters | Moderator | Broadcaster | Owns Ko-fi and the other supporter feeds. |
| Federation | Broadcaster | Broadcaster | |
| API & devices | Editor | Broadcaster | Was "Automation": API tokens, Stream Deck pairing, device codes. |

**Setup** (pinned to the bottom, collapsed)

| Page | Read | Manage | Notes |
|---|---|---|---|
| Integrations | Broadcaster | Broadcaster | The one home for every credential: Twitch app credentials (3 places become 1), the bot account, Spotify, OBS, VTube Studio, Discord, YouTube, Kick, the TTS providers. |
| Features | Broadcaster | Broadcaster | |
| Settings | Moderator | Broadcaster | 6 tabs: General; Language & fonts; Notifications; Bot behavior; Privacy; Danger zone. Billing is a seventh tab in saas mode only (saas is a RESTRICTED option, reserved to NoMercy Labs under the licence). |

**Outside the sidebar**

- My data: profile menu, floor Everyone.
- Admin: profile menu and Ctrl+K, platform admins only. Entering Admin swaps the whole chrome (slice S095), so it is clear you are not in a channel.

Counts (the placement ledger has every row):

- The broadcaster sees 34 sidebar rows (Home 1, Live 6, Build 8, People 6, Loyalty 4, Music & Voice 3, Connect 6) plus 3 Setup rows when expanded: 37 at most, instead of 45 plus Admin.
- 11 old pages become tabs: Multi-Chat, Schedule, Alerts, Chat Triggers, Voice Triggers, Timers, Sound Clips, Assets, Pick Lists, Quotes, Song Requests.
- My Data and Admin leave the sidebar. Bundles joins Gallery. The 4 Moderation routes and Community become People tabs. Roles joins People.
- 2 new pages: Deck and Gallery. Library replaces 4 pages.

### 3.3 Pins

Each role gets 8 default pins at the top of the sidebar. The user can change them. The choice is stored on the account, so it follows the user to every device.

| Role | Default pins |
|---|---|
| Broadcaster | Home, Chat, Deck, Commands, Event Responses, Overlays, People, Music |
| Moderator | Home, Chat, People: Queue, People: Viewers, People: History, Music: Song requests, TTS, Deck |
| Lead Moderator | The Moderator pins, with People: Rules in place of TTS |
| Editor | Home, Commands, Event Responses, Triggers & Timers, Overlays, Pipelines, Library, Chat |

### 3.4 Search (Ctrl+K)

One palette indexes:

- every page and every tab;
- every Deck button (run it from the palette);
- every command, overlay, pipeline and script, by name;
- every viewer, by name, on every platform;
- every channel the user can reach (switch channel);
- the Admin pages, for platform admins.

The palette respects floors. It never lists what the user cannot open.

## 4. The Deck: everything on stream, one tap away

The owner asked that everything is "right in front of you when you need it the most or when you want to create some shenanigans on stream with all of its features".

Benchmark evidence (section 10): Streamer.bot, Firebot, SAMMI, Mix It Up and Lumia all split "set up" from "use live". SAMMI and Lumia have a button deck. The Twitch Stream Manager has "Quick Actions" tiles. No web bot (Nightbot, Fossabot, StreamElements, Streamlabs) has a live deck.

### 4.1 What a button can do

A Deck button is a generic primitive: it binds to one action from the existing action catalogue. No feature gets a bespoke button type.

- Run a pipeline (this covers every multi-step shenanigan).
- Play a sound from the Library.
- Speak a TTS line.
- Test an event response (fire a fake follow, sub, raid).
- Show or hide an overlay.
- Send a chat message or an announcement.
- Start a poll or a prediction from a preset.
- Raid, run an ad, place a marker, make a clip.
- Chat modes: emote-only, sub-only, slow, followers-only, Shield mode.
- Switch an OBS scene, toggle a source, start or stop recording.
- Fire a VTube Studio hotkey.
- Start a game or a giveaway.
- Pause or resume a timer.
- Skip a song, pause the music, clear the TTS queue.

### 4.2 Rules

- Each button carries its own floor. A Moderator sees the buttons at or below their role. A button above their role is not shown.
- Editing the Deck needs Moderator (owner 2026-10-11).
- A button with a risky action (ban wave, raid, ad) asks for a second tap. Raid and ad buttons need Editor (owner 2026-10-11).
- A button shows its live state from the event stream (for example, "Shield on", the current scene, the song playing).
- The layout is stored per channel, with an optional private layout per user. It needs one small table in BOTH migration sets (Postgres and SQLite).

### 4.3 Where the Deck shows

- Live, Deck: the full grid.
- Home, Live layout: the first 8 buttons.
- Phone: a full-screen grid, one tap per action.
- Desktop: a pop-out window for the second monitor. This replaces a TV layout.
- Stream Deck: the plugin from `spec/stream-deck.md` (D1-D8) shows the same Deck. It calls `POST /automation/v1/invoke` and reads key state from the event stream. No new server tables beyond the layout.
- Ctrl+K: every button by name.

## 5. Who sees what

### 5.1 The ladder

Viewer, Subscriber, VIP, Artist, Moderator, Lead Moderator, Editor, Broadcaster. Platform admin is a separate plane (IAM), not a channel role. Users see the names, never numbers.

### 5.2 What each role uses most (importance ranking)

The ranking comes from the owner's persona order, the benchmark (section 10) and the stream state (live or offline). Rank 1 is the most important.

**Broadcaster, live:** 1 Chat, 2 Deck, 3 Home Live layout (stream health, attention inbox), 4 Stream: Interactions, 5 OBS, 6 Music, 7 People: Queue, 8 TTS Live.

**Broadcaster, offline:** 1 Home Prep layout, 2 Commands, 3 Event Responses, 4 Overlays, 5 Pipelines, 6 Library, 7 Analytics, 8 Integrations.

**Moderator:** 1 Chat (with "All my channels" for moderators of many channels), 2 People: Queue, 3 People: Viewers, 4 People: History, 5 Song requests, 6 TTS Live, 7 Deck (moderator buttons), 8 Commands (read, simple replies).

**Lead Moderator:** the Moderator list, plus People: Rules, assigning Moderators and VIPs, and Discord settings.

**Editor:** 1 Commands, 2 Event Responses, 3 Overlays, 4 Pipelines, 5 Triggers & Timers, 6 Library, 7 Stream: Info & presets, 8 Loyalty pages.

**Viewer:** 1 chat commands in the streamer's chat (the main viewer UI), 2 the public commands page, 3 song request page, 4 points and store, 5 games, 6 leaderboards, 7 Me (TTS voice, pronouns, linked accounts, my standing), 8 My data.

**Platform admin:** the Admin plane (23 tabs), entered from the profile menu.

### 5.3 The viewer side

- The viewer shell stays separate from the operator shell: My channels (cards), and per channel Now playing, Leaderboards, Points and store, Games, Me.
- My data is in the viewer profile menu too.
- Public pages that need no sign-in stay plain HTML served by the server, not the Compose app: the channel commands page (`/c/<login>`), the song request page, the leaderboard. Section 9 gives the reason (load and scale).
- A viewer never sees a moderator tool. A viewer who is also a moderator in another channel sees the operator shell for that channel only.

### 5.4 Differences laid out

| Need | Viewer | Moderator | Lead Moderator | Editor | Broadcaster |
|---|---|---|---|---|---|
| Chat and Shield toggle | own chat client | yes | yes | yes | yes |
| Deck | no | moderator buttons, and edit | moderator buttons, and edit | all, and edit | all, and edit |
| Stream info and interactions | no | edit, except content labels, branded content, ads and raids | edit, except content labels, branded content, ads and raids | edit | edit |
| Build pages | public commands list | edit | edit | edit | edit |
| Scripts | no | yes | yes | yes | yes |
| People: Queue, Viewers, History | own standing only | yes | yes | yes | yes |
| People: Rules | no | read | edit | read | edit |
| People: Roles | no | read | assign Moderator, VIP | read | all |
| Loyalty | points, store, games | edit | edit | edit | edit |
| Music & Voice | request songs, pick TTS voice | queue control and settings (not TTS Provider) | queue control and settings (not TTS Provider) | settings | settings |
| Connect | no | Discord read | Discord edit | Custom events, API read | all |
| Setup | no | Settings read | Settings read | Settings read | all |
| My data | yes | yes | yes | yes | yes |

## 6. Layout per screen size

- Wide (840 dp and up): sidebar with groups and pins. The viewer card opens in a side panel, so chat stays visible.
- Medium (600 to 839 dp): an icon rail. The viewer card opens in a sheet.
- Phone (under 600 dp): a bottom bar with 4 role-pinned items and "More". "More" opens a sheet with the full sidebar. The Deck is a full-screen grid.
- No TV layout. The Deck pop-out covers the second monitor.

## 7. More than one platform

- Chat and People have platform chips: Twitch, Kick, YouTube, X. "All" is the default.
- Stream: Info & presets edits each platform connection, with "All platforms" to set one title everywhere.
- A platform-specific action is disabled with the reason ("Not on Kick"). It is never hidden.
- The viewer card shows the person on every linked platform.

## 8. Moving from today to the new layout

- V0 (owner 2026-10-11): the new routes replace the previous ones outright. There is no alias map and no redirect code.
- The new address shape: `#/c/<login>/<page>[/<tab>]`, so a link always opens the right channel.
- Saved pins and expanded groups start from the role defaults (section 3.3). They are not carried over.
- No user is logged out (standing rule: never log out for a schema or scope change).
- A test proves that every page and tab in section 3.2 opens at its new address.
- `spec/frontend-ia.md`, `FEATURES-BY-SIDEBAR.md` and `FEATURES.md` are updated in the same slice.

### 8.1 Empty states and first use

- A feature page that needs a connection shows the connect step inline (no trip to Integrations first). Integrations stays the one home of the settings.
- Every create flow starts with "Start from a template" or "Start empty".
- A new channel gets starter content: a few commands, event responses and a starter Deck (benchmark: Streamer.bot, Firebot and Mix It Up ship starter content).

## 9. Verdict: is Kotlin Multiplatform still right for 100,000 users at once?

### 9.1 The verdict

Keep Kotlin Multiplatform and Compose for the operator dashboard (streamers, moderators, editors). Do not use Compose for the public viewer pages. Fix the first-load size now. Look again in February 2027.

### 9.2 Why: who the 100,000 users are

- The dashboard is for operators. Viewers mostly use chat commands, and OBS loads overlays. Neither opens the Compose app (`.claude/docs/reference/frontend-architecture.md:25-33`, Lane L5).
- The pages viewers open in a browser (song requests, overlays, the landing page) are already plain HTML served by the server (`OverlayHostController.cs:35`, `Program.cs:965-970`).
- So most of the 100,000 never download the Compose bundle. Their load lands on the server: chat, events, the realtime hub and the database.

### 9.3 Why: the server is the real limit

- The realtime hub has no backplane. `spec/platform-conventions.md:434`: "SignalR - MessagePack and backplane NOT BUILT ... hub fan-out is per instance."
- `Program.cs:189-205` sets up SignalR with JSON only. MessagePack is smaller and faster.
- Many server nodes behind a load balancer need EventSub conduits. Conduits are opt-in (`EventSub:Conduits:Enabled`, default off).
- A rewrite of the client in React or anything else changes none of this. Slice S-SCALE-BACKPLANE fixes it.

### 9.4 Why keep Compose for the dashboard

- 239,000 lines of Kotlin are in the app: 146,817 in commonMain, 87,950 in tests (counted 2026-10-11).
- The screens alone are 110,374 lines; the API client 20,289; the design system port 9,601.
- A React or Svelte rewrite throws away all of it, including 377 test files. A hybrid (Compose desktop plus a DOM web app) keeps two UIs for every feature, forever.
- Desktop Compose is Stable. The desktop app is the same code.
- JetBrains names "internal dashboards, admin tools, or back-office apps" as the intended web use (https://lp.jetbrains.com/cmp-for-web/). Our dashboard is exactly that: signed-in operators, no SEO need.

### 9.5 The risks we accept, and what we do about each

| Risk | Fact | Action |
|---|---|---|
| Web is Beta | CMP web is Beta; Kotlin/Wasm is not Stable. The Aug 2026 Kotlin roadmap still lists "Promote Kotlin/Wasm to Stable" as a plan. | Upgrade from CMP 1.9.0 (Kotlin 2.2.21) to 1.12.1 (Kotlin 2.3.20): S-KMP-CMP112. Re-decide at the February 2027 roadmap update. |
| First load is heavy | Measured live 2026-10-11: about 46 MB on the first visit. Code is 10.7 MB (app wasm 6.66 MB, skiko 3.85 MB, both brotli). Fonts are 35.2 MB, sent without compression, all 10 preloaded (Chinese 17.8 MB, Korean 10.4 MB). | S-KMP-FONTS: compress fonts, load a language font only when that language shows, cache `composeApp.js` (sent `no-store` today). Target: 11 MB or less on the first visit. A repeat visit is already about 0.2 MB. |
| Accessibility is basic | Tested live: the a11y tree exists, but its box is 0 by 0, there are no headings, one field has no name, and the whole screen is one live region. Browser find matches hidden text but shows no highlight. | S-KMP-A11Y plus the CMP 1.12 upgrade (it fixes the 0 by 0 box, issue #3226). |
| Safari | Kotlin docs: Safari 18.2 and up. caniuse marks every Safari "no" for WasmGC. The two sources conflict. | S-KMP-FALLBACK: a clear "browser not supported" page, or the JS compatibility build. |
| Old browsers get a blank app | There is no JS fallback in our build (`build.gradle.kts:54`, wasmJs only). | S-KMP-FALLBACK. |
| Wasm-only build breaks | `jvmTest` cannot catch a wasm-only break. | Already gated: `compileKotlinWasmJs` before every app commit. |

### 9.6 What would change the verdict

- Kotlin/Wasm is still not Stable after the February 2027 roadmap update.
- The first load cannot get under about 11 MB after S-KMP-FONTS.
- Safari users cannot open the dashboard after S-KMP-FALLBACK.
- Viewers, not operators, turn out to be the main users of the Compose app. Then the viewer pages move to plain HTML first.

If one of these happens, the cheapest path is: keep Kotlin for the API client and logic (Kotlin/JS), and build only the web screens in a DOM framework. Not a full rewrite.

### 9.7 Alternatives compared (Lane L5)

| Option | Fit | Why not now |
|---|---|---|
| Stay on Compose web, upgrade to 1.12.1 | Best fit | Chosen. |
| React or Next.js | Mature, native DOM a11y, small runtime (react-dom 64 KB gzip) | A full rewrite of 239k lines and 377 test files. Desktop needs Electron or Tauri. |
| SvelteKit | Small, native DOM | The same full rewrite, and no first-party shadcn new-york port. |
| Flutter web | Canvas like Compose, with the same a11y trade | A full rewrite into Dart for the same model. |
| Kotlin/JS with a DOM UI (Compose HTML, Kobweb, Kilua) | Keeps the Kotlin API client | Compose HTML is experimental; Kobweb and Kilua are third party. Desktop needs a second UI. |
| Hybrid: Compose desktop plus a DOM web app | Native web a11y | Two UIs for every feature, forever. |

Comparable products (Streamlabs, StreamElements, Twitch, Discord) use React or Vue in the DOM. None of them is a canvas app. That is a fact about their choice, not proof that ours is wrong: they serve viewers in the browser; our viewers are served by plain HTML pages and chat.

## 10. Benchmark lessons

Full table: `benchmarks/dashboard-ia.md`. Lane notes L4a (desktop bots), L4b (web bots), L4c (platform tools).

- Trigger and action: Streamer.bot, Firebot and Mix It Up build every automation as "when this happens, do that". Our Build group follows this: triggers, event responses and commands all lead into the same actions and pipelines.
- A live deck apart from setup: SAMMI and Lumia ship a button deck. The Twitch Stream Manager has Quick Actions. We add the Deck (section 4).
- Starter content: Streamer.bot, Firebot and Mix It Up ship examples. We add a starter Deck and starter commands (S-IA-STARTER).
- Panels with an edit mode: the Twitch Stream Manager lets the user move panels and saves the layout. Our Home Live layout gets the same, stored on the account.
- The moderator view is a subset of the streamer view: Twitch Mod View, YouTube Live Control Room and Kick show the same page with fewer tools. We do the same: one shell, gated by role.
- Layouts stored on the account: Twitch and YouTube keep the layout across devices. Our pins and Deck layout are stored on the account.
- Web bots (Nightbot, Fossabot, StreamElements) group by feature list and have no live surface. That is the pain we leave behind.

## 11. Conflicts this paper settles

Each conflict was found between the specs, the plan and the code (Lane L3). The left side loses.

| Conflict | Settled as | Why |
|---|---|---|
| Slice S072's sidebar groups vs the owner's 2026-09-08 navigation sentence | The new groups (section 3), with Build | The owner sentence is newer and names the pain. S-IA-SPEC replaces S072's group list. |
| S106 (stream info on its own page) vs the usability finding H1c (stream info on Home) | Both: one Stream Info editor component, shown on Live, Stream and on Home Prep | No second editor; two entry points for two moments. |
| Alerts page vs Event Responses page | Event Responses absorbs Alerts | One page per job: answer an event. |
| Sound Clips vs Assets | Library with tabs Sounds and Files | Same read key, same users. |
| Ko-fi on Webhooks and Supporters | Supporters | Ko-fi is a supporter feed; Webhooks stays generic. |
| Twitch app credentials in 3 places | Setup, Integrations only | The settled rule: Integrations owns connections. |
| My Data floor Moderator | Everyone, in the profile menu | Every viewer owns their data. |
| "Super Mod" label | "Lead Moderator" | Owner 2026-06-23. |
| Stream presets inside a dialog on Home | Live, Stream: Info & presets | The same component on Home Prep. |
| Templates page vs create flows | Gallery page plus "Start from a template" in every create flow | Templates are found where you create. |
| S080 profile menu contents vs the newer plan | The newer plan: identity, theme, language, My data, Admin | The newer decision wins. |
| OBS connection on the OBS page | Integrations, with the connect step inline on OBS when not connected | Feature pages carry no wire-up controls. |
| Commands manage floor Moderator today | Moderator for every command | Owner 2026-10-11: "moderators get granted the permission to manage stream and channel behaviors". The server already has commands:write at Moderator (ActionDefinitionSeeder.cs:139). |
| 35 server actions sit at Editor, among them stream presets and schedule, title, category, tags, language, extensions, content labels, branded content, predictions, raids, ads, music settings, economy and bundles (ActionDefinitionSeeder.cs) | Moderator for the stream and channel behavior actions (slice S-IA-MOD-FLOORS) | Owner 2026-10-11: "moderators get granted the permission to manage stream and channel behaviors". Connect wiring (webhooks, egress, integrations, API tokens, EventSub) stays at Editor. Owner 2026-10-11: "we will leave the risky items to the channel manager and up": content labels, branded content, ads and raids stay at Editor. |

## 12. Defects found while researching

Each was seen live or read in code on 2026-10-11. Each has a slice.

- Fonts: all 10 font files are preloaded and sent without compression: 35.2 MB on the first visit (S-KMP-FONTS).
- `composeApp.js` is sent with `Cache-Control: no-store`, so it downloads on every load (S-KMP-FONTS).
- `https://dev.nomnomz.bot/app/` (with a trailing slash) shows a blank app. Its scripts resolve to `/app/composeApp.js`, which returns 404. `/app` works. `index.html` has no `<base>` tag (S-KMP-BASEHREF).
- The web sign-in shows a "Backend URL" field. UF F7 says this field is for desktop only; the web app is single-origin (S-IA-SETUP).
- The "Backend URL" field has no accessible name and is marked multi-line (S-KMP-A11Y).
- The whole sign-in screen is one live region, so a screen reader may re-read it on each change (S-KMP-A11Y).
- The a11y tree box is 0 by 0. Browser find matches text, but the highlight is invisible (S-KMP-CMP112 fixes the box).
- The UI says "Super Mod" (S-IA-NAV).
- The app hides Code Scripts from a Moderator (nav floor Broadcaster), but the server lets a Moderator write scripts (ActionDefinitionSeeder.cs:179). The app and the server must show the same floors (S-IA-MOD-FLOORS).
- 48 of 70 app controllers do not refresh live (S-IA-LIVE-EVERYWHERE).
- `https://nomnomz.bot/` returns 404. The public domain has no landing page yet. This is a deploy item, not a UI item; it is listed for Stoney.

## 13. The one-shot plan

The order prevents shuffling: the spec and the addresses land first, so every later slice puts its page in a place that is already decided and already routable.

**Step 1: settle (one slice, needs Stoney's yes)**

- S-IA-SPEC: `spec/frontend-ia.md` gets the layout of section 3, the floors, the pins, the Deck and the conflict table of section 11. `FEATURES-BY-SIDEBAR.md` follows. No code until Stoney says yes.

**Step 2: the base (two slices, in order)**

- S-IA-ROUTES: the new page and tab list and the `#/c/<login>/<page>[/<tab>]` address replace the previous routes, with no alias map (V0). A test opens every page and tab. No logout.
- S-IA-NAV: the new groups in the sidebar, the new names (Lead Moderator, Channel Points, Overlays, Random responses, Individual permissions), Admin moved into the profile menu with the chrome swap.

**Step 3: parallel lanes (each lane owns its own feature folders; at most 2 build at a time)**

- S-IA-PEOPLE (this is S-USER-HUB): the People screen with 6 tabs; Roles moves in.
- S-IA-LIBRARY: Library with 4 tabs; upload inline in every picker.
- S-IA-TRIGGERS: Triggers & Timers with 3 tabs; Event Responses absorbs Alerts.
- S-IA-SCRIPTS: Scripts opens straight into the editor.
- S-IA-GALLERY: Gallery with Templates and Bundles; "Start from a template" in every create flow.
- S-IA-DECK: the Deck table in both migration sets, the API, the page, the phone grid, the desktop pop-out, the Home top 8, the Stream Deck plugin reads the same Deck.
- S-IA-STREAM: Live, Stream with 3 tabs, per platform; Home Prep embeds the same editor.
- S-IA-MEDIA: Music with Song requests as a tab; TTS with 4 tabs; Media Share; OBS and VTube Studio with tabs and inline connect.
- S-IA-MOD-FLOORS: the server lowers the stream and channel behavior actions from Editor to Moderator, and the app shows the same floors. Content labels, branded content, ads and raids stay at Editor.
- S-IA-SETUP: Integrations as the one credential home; Ko-fi to Supporters; Automation renamed "API & devices"; Settings with 6 tabs; My data in the profile menu; the web sign-in hides "Backend URL".
- S-IA-GAPS: the server operations that need a screen, as placed in the placement ledger.

**Step 4: finish (after the lanes)**

- S-IA-PINS: default pins per role, stored on the account.
- S-IA-PALETTE: Ctrl+K over pages, tabs, Deck buttons, commands, viewers and channels.
- S-IA-ROLE-HOMES: the streamer Prep and Live layouts, the Moderator home, the viewer home; public viewer pages as plain HTML where missing.
- S-IA-COMPACT: the phone bottom bar and "More" sheet; the medium icon rail.
- S-IA-PLATFORM: platform chips; platform-only actions disabled with the reason.
- S-IA-LIVE-EVERYWHERE: every page refreshes live; a script proves 70 of 70 controllers listen.
- S-IA-STARTER: starter commands, event responses and a starter Deck for a new channel.

**Guard (lands with S-IA-ROUTES, runs on every later slice)**

- S-IA-GUARD: a script fails when a page or tab has no row in the placement ledger, or a ledger row names a page that has no route.

**KMP and scale lane (any time, independent of the steps)**

- S-KMP-FONTS: compressed fonts, a language font loads only when needed, `composeApp.js` cacheable. Target: first visit 11 MB or less, measured live.
- S-KMP-CMP112: CMP 1.12.1 and Kotlin 2.3.20; retest the 3 wasm workarounds (fonts, shadow root, WebSockets).
- S-KMP-FALLBACK: an unsupported browser gets a clear page, or the JS compatibility build.
- S-KMP-A11Y: field names, headings, no whole-screen live region; zoom and Safari tested by hand.
- S-KMP-BASEHREF: `/app/` works the same as `/app`.
- S-SCALE-BACKPLANE: SignalR with a Redis backplane and MessagePack, so many server nodes share one realtime hub.

## 14. Coverage ledger

Every topic this paper set out to cover, with its status. Closed means a section settles it from a file that was read or a page that was measured. Blocked means no source read settles it; the reason names what is missing. The full per-item placement is in `ui-placement-ledger-2026-10-11.md`; the benchmark table is in `benchmarks/dashboard-ia.md`.

Closed 24 of 28. Blocked 4.

| Topic | Origin | Status | Section | Reason |
|---|---|---|---|---|
| Every app surface counted (pages, tabs, cards, dialogs, actions) | owner + code | closed | 1.1 |  |
| Every server capability counted, with its UI place or gap | owner + code | closed | 1.2 |  |
| Every viewer feature (commands, points, song requests, TTS, games, economy, portal) | owner | closed | 5.3 |  |
| Role ladder: viewer, VIP, moderator, lead moderator, editor, broadcaster, admin | owner + code | closed | 5.1 |  |
| Settled UI decisions and owner words | code | closed | 2 |  |
| Known UI defects and contradictions | code | closed | 12 |  |
| Desktop bot benchmark (Streamer.bot, Firebot, Mix It Up, SAMMI, Lumia) | benchmark | closed | 10 |  |
| Web bot benchmark (Streamlabs, StreamElements, Nightbot, Fossabot, Moobot, Wizebot, Botrix) | benchmark | closed | 10 |  |
| Platform tools (Twitch Stream Manager, Mod View, YouTube Studio, Kick, Discord, OBS docks) | benchmark | closed | 10 |  |
| Live-on-stream access: what must be one click away | owner | closed | 4.1 |  |
| Shenanigans: fun features surfaced live (Deck) | owner | closed | 4 |  |
| Importance ranking of each feature per role | owner | closed | 5.2 |  |
| Hierarchy: new sidebar groups, pages, tabs | owner | closed | 3.2 |  |
| Moderator view vs streamer view vs viewer portal | owner | closed | 5.4 |  |
| Multi-platform (Twitch, Kick, YouTube, X) in the structure | code | closed | 7 |  |
| Phone, tablet, desktop, TV layouts | code | closed | 6 |  |
| Search, command palette, favourites, pinning | benchmark | closed | 3.3 |  |
| Onboarding and empty states in the new structure | benchmark + code | closed | 8.1 |  |
| Migration: route renames, deep links, saved state, no logout | code | closed | 8 |  |
| One-shot execution plan: order, slices, gates | owner | closed | 13 |  |
| KMP verdict: Compose web status, a11y, bundle, load, browsers | owner + outside source | closed | 9.5 |  |
| KMP verdict: our own defect record on wasm | code | closed | 9.5 |  |
| Scale: what serves 100k concurrent users (server, realtime, CDN) | owner + outside source | closed | 9.3 |  |
| Alternatives: React or Next, Svelte, Flutter web, Kotlin/JS | outside source | closed | 9.7 |  |
| Browser zoom at 200 percent on the web dashboard | code | blocked |  | A real zoom key press is GUI automation of the owner machine, which the owner rule forbids; slice S-KMP-A11Y tests it by hand. |
| Safari 18.2 and later running the dashboard | outside source | blocked |  | No Safari on this machine; Kotlin docs and caniuse conflict; slice S-KMP-FALLBACK tests it on a Mac. |
| CMP 1.12.1 fixes our 0 by 0 a11y box and font fallback | outside source | blocked |  | Needs the upgrade spike in a worktree build; that is slice S-KMP-CMP112, not research. |
| How many operators vs viewers open the Compose app | owner | blocked |  | No traffic data exists yet; the server has no per-surface visit count. |
