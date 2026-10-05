# NomNomzBot user flow: behavioral change spec

> Binding input for the **UF** slices in `SHORTCOMINGS-EXECUTION-PLAN.md` (`S-UF-*`). Behavior only: what the product must do, not how to code it. Researched from source at `cffd5d7b2`, 2026-10-03; re-checked against `22072b7b3` when the slices were planned. Readable and narrated copy: https://claude.ai/artifact/AgV92MvRWCNjipL5APn92w

Oct 3, 2026 · @Stoney

NomNomzBot serves four people in four states of mind with one calm, settings-style interface: a streamer preparing, a streamer performing, a moderator under pressure, and a viewer in chat. It does not change shape between them. The five changes that matter most:

1. **End setup on proof.** The first run ends with the bot answering `!ping` in the streamer's own chat, confirmed on screen, within 2 minutes (F3). Today nothing ever says the bot works.
2. **Give Home a live mode.** Viewers, uptime, the ad countdown and the queues lead while live; preparation leads while offline (H1). Today the page is identical in both states and its numbers freeze after going live.
3. **Never lose work or points silently.** Editors stay open until the server answers. Changed forms ask before closing. Failed redemptions refund (A1, V4).
4. **Undo instead of confirm.** One safety ladder decides between none, Undo, delayed send, confirm and type-to-confirm by reversibility and reach (X2). Today a cross-channel ban costs as little as a message delete, and a held-message ban fires in one click.
5. **Answer every viewer, briefly.** Cooldowns say how long, denials say who can, giveaways and games report in chat, and replies are prioritized so the bot never floods or answers late (V1 to V6).

How to read it: each change has an ID (F first run, N navigation, H home, A authoring, O overlays, M moderation, I integrations, V viewers, X cross-cutting). Each says what happens **today**, citing the source file and line at commit `cffd5d7` (3 Oct 2026), then the exact behavior to build, and often a **done when** check. The P codes refer to the principles in the next section. This spec covers behavior; correctness bugs stay in the existing usability ledgers, which it cites rather than repeats. It was written from the source code, not from a running instance, so rendered details (exact pixel layout, scroll behavior) should be checked on the client when each item is built.

## Who uses it, and in what state of mind

The same person uses NomNomzBot in very different mental states, and most current friction comes from serving every state with one calm, settings-style interface. Every change in this spec is tagged with the state it serves and the principle it applies.

| Person and state | Where their attention is | What they need from the app | What breaks them |
| --- | --- | --- | --- |
| Streamer, offline (prep) | Fully on the dashboard, minutes to spare | Confidence that what they build will work live, without embarrassment | Blank forms, jargon, no way to see the result before going live |
| Streamer, live (performing) | Camera, game and chat first; dashboard on a second monitor or phone, in 1 to 2 second glances | One-tap actions, status readable at a glance, nothing that asks to be read | Dialogs, small targets, pages that look the same live and offline |
| Moderator, live (vigilance) | Chat feed, often for several channels, under time pressure | Context at the moment of judgement, speed, a way back from mistakes | Hover-only actions, confirmations on every click, losing their place, alert floods |
| Viewer, in chat | The stream and chat, never the dashboard | To be noticed within seconds, to know why something did not work, to feel the queue is fair | Silence after a command, bot spam, opaque queues and draws |

### Principles applied

| Code | Principle | What it means in NomNomzBot |
| --- | --- | --- |
| P1 | Mode matching | The dashboard has a prep layout and a live layout, and switches when the stream state changes. |
| P2 | Time to first value | A new streamer sees the bot answer in their own chat within two minutes of signing in, before any configuration. |
| P3 | Defaults are decisions | Every choice arrives pre-filled with the recommended answer; the user confirms instead of composing. |
| P4 | Recognition over recall | Variables, users, commands, sounds and widgets are picked from lists, never typed from memory. |
| P5 | Preview before commit | Every authored response shows the exact chat line, alert or TTS line it will produce, with real sample data, before saving. |
| P6 | Undo over confirm | Reversible actions run immediately with a timed Undo; confirmation dialogs are reserved for actions nothing can reverse, so they keep their meaning. |
| P7 | Feedback where the eye is | Every action changes something visible at the spot the user acted, within 1 second; nothing succeeds or fails silently. |
| P8 | Progressive disclosure | Each persona sees the few surfaces they use weekly; the rest sits one search or one click away, not in a 45-item sidebar. |
| P9 | Targets for stress | Live actions are large, keyboard-reachable and never hover-only (Fitts's law under divided attention). |
| P10 | Signal over noise | One ranked attention inbox, duplicates collapsed, severity decides whether something interrupts. |
| P11 | Be seen, be fair | Viewers get a personal acknowledgement fast, see their place in any queue, and can see how draws are decided. |
| P12 | Familiar conventions | Where streamers already know a pattern from other chat bots (`!commands`, `{user} variables`, `!sr`), keep that pattern. |

## First run: sign-in, setup and the first "it works"

Today nothing in the app ever tells a new streamer that the bot works. On self-host the first working reply in chat takes 10 or more clicks, two Twitch sessions and 5 to 15 minutes, and the wizard blocks on a bot account that the copy calls optional. The changes below cut setup to the decisions that cannot be defaulted and end it on a visible proof.

### F1. Ask why they came before installing anything (P3)

Today: any login without an owned channel creates and onboards a channel, so a viewer who signs in for the D4 free account becomes a broadcaster with the bot installed in their own chat (`server/.../AuthService.cs:375-397`).

Change: after the first sign-in, show one screen, "What do you want to do?", with three cards:

- **Run the bot on my channel** installs the bot.
- **Moderate a channel** lists the channels the user moderates that already have the bot.
- **Manage my viewer profile** opens the viewer home (F8).

The recommended card is pre-selected:

- Moderate, when the user already moderates a channel that has the bot installed.
- Run the bot, in every other case.

Only **Run the bot on my channel** creates a channel, subscribes EventSub topics and seeds defaults. The choice can be changed later from the profile menu ("Add the bot to my channel").

### F2. A two-step self-host wizard that remembers where you were (P3, P2)

Today: 6 steps, in this order: Twitch app, bot account (required, blocks Next at `SetupController.kt:470-474`), Spotify, Discord, YouTube, Review. The review step asks for a BCP-47 language code and an IANA timezone as free text. Progress lives only in memory, and a reload after step 1 skips the rest of the wizard for good (`App.kt:132`).

Change: the wizard keeps only what cannot be defaulted.

1. **Twitch app.** **Use the shared NomNomzBot app (recommended)** is the pre-selected primary button. "Use my own Twitch app" is a collapsed secondary option with one line: "For full independence from NoMercy. Takes about 5 minutes on dev.twitch.tv." Client ID and secret appear only after expanding it, with numbered steps and the exact redirect URL to paste, plus a Copy button.
2. **Sign in as the streamer.** This creates the channel.

Everything else leaves the wizard:

- The bot account moves to F4.
- Spotify, Discord and YouTube move to the Get started checklist and Integrations.
- Command prefix is `!` with no question asked.
- Language comes from the browser locale, timezone from the browser's zone. Both are editable later in Settings as a dropdown and a searchable picker.

Wizard progress is saved on the server after every step. A reload resumes at the same step with the typed values restored. `onboardingComplete` becomes true only after step 2 succeeds.

### F3. End setup on a proof, not a dashboard (P2, P7)

Today: after sign-in the user lands on Home. The bot may or may not be in chat, and may or may not be a moderator (on self-host it is only modded when `Twitch:BotUsername` is configured, which the wizard never asks for).

Change: the last screen of onboarding is **Your bot is in your chat**, and it shows live status as it happens:

1. A row per platform: "Joining chat…", then "In chat", then "Moderator" or "Not a moderator yet". It updates from the hub, not by polling.
2. If the bot is not a moderator, show the exact line to type, `/mod nomnomzbot`, with a Copy button and one sentence on why: "Moderators aren't slowed down by Twitch's chat limits." The row turns green as soon as the bot sees the mod grant.
3. A primary button, **Say hi in chat**. The bot types "NomNomzBot is here. Try !commands" (editable before sending) and a small embedded chat shows the line arrive.
4. A prompt, "Now type !ping in your chat." When the bot sees it, the screen confirms "It works. The bot answered in 0.4 s." with the real latency.
5. Two buttons follow: **Set up alerts** (primary, goes to F5) and **Go to dashboard** (secondary).

The screen can be skipped with "I'll do this later", which marks Get started step 2 as not done. Done when a new SaaS streamer reaches the confirmed `!ping` within 2 minutes of the first click.

### F4. The bot account is optional, and the choice is shown as the chat line it produces (P3, P5)

Today: the bot account step cannot be skipped on self-host. It tells the user to log into Twitch as the bot in the same browser. Then the streamer sign-in uses `forceVerify: false` (`AuthService.cs:172`), so Twitch can silently authorize the bot account as the streamer.

Change:

- The bot types as the streamer's own account until a bot account is connected. The D5 line prefix defaults to `*`.
- Offer the bot account in two places: as an optional Get started step, and in Integrations. Present it as a choice between two rendered chat lines, not as an account concept:
  - "Stoney\_Eagle: \* Thanks for the follow, Ana!" (your account, with marker)
  - "NomNomzBot: Thanks for the follow, Ana!" (separate bot account)
- Both the streamer sign-in and the bot sign-in use `force_verify=true`. After each one, show the account Twitch returned: "Signed in as NoMercyBot\_. Is this your bot account?" with **Yes** and **Use a different account**. If the bot sign-in returns the streamer's own account, refuse it with "That's your streamer account. Sign in with the bot's account instead."
- The bot sign-in instructions say "Open twitch.tv/activate in a private window and log in as the bot there." That way the user's main Twitch session stays the streamer.

### F5. Defaults that do something on the first stream (P2, P3)

Today: the follow, sub, gift sub, resub, cheer and raid chat lines are on from the first minute. They are on through the platform defaults (`PlatformEventResponseDefaultsSeeder.cs:29-48`), even though each channel row is stored disabled and only follows that default (`EventResponseDefaultsSeeder.cs:85-88`). Nothing tells the streamer, so the first time they learn about these lines is when the bot types one live. Every recognition response for regulars starts off: first-time chatter, returning chatter, watch streak. `!sr`, `!skip`, `!queue`, `!volume` and `!song` are seeded **enabled** with no music provider connected (`DefaultCommandsSeeder.cs:47,90`).

Change:

- Keep the core alert chat lines on, and show them, by name, on the **Set up alerts** screen that follows F3. Each line gets its own toggle and the chat line it produces, rendered in the channel's tone with a sample name: "Welcome to the raid, Ana and her 12 viewers!" The user never discovers a line the bot typed without being told it would.
- On the same screen, offer two recognition responses, pre-checked: **Welcome first-time chatters** and **Celebrate watch streaks**. Returning-chatter greetings stay unchecked, because in a busy chat they are the ones most likely to read as spam.
- Overlay output and sounds for these events stay off until the user adds the alert overlay (Get started step 3).
- Seed music commands **disabled**. When the first music provider connects, show one prompt: "Turn on song requests? Viewers can use !sr in chat." with **Turn on** and **Not now**.
- Seeded fun presets (`!8ball`, `!hug`, ...) stay enabled, but are tagged "Starter" in the Commands list. They do not count as the user's own commands for the Get started checklist. Today they hide that card entirely (`HomeController.kt:178`).

### F6. Ask for Twitch permissions at the moment they are needed (P4)

Today: the Features page shows raw scope strings ("Required scopes: channel:read:redemptions ...") and a **Grant scopes** button on every row (`FeaturesScreen.kt:206-245`). Scopes appear in four places (the Integrations banner, Settings Permissions, Settings White-label and Features). Toggling a feature never checks scopes, so it fails later, at use.

Change:

- Toggling on a feature that lacks a scope opens a small inline step instead of switching on: "Channel points needs permission to read and manage your rewards on Twitch." with **Allow on Twitch**. After the grant returns, the toggle completes by itself. Cancelling leaves it off.
- Scope strings move behind a "Technical details" disclosure.
- Keep one Permissions view (in Settings). The other three places link to it.

### F7. Honest errors and a fast return (P7)

Today: an unreachable server reads "Sign-in failed. Please try again." (`ConnectController.kt:386`). An encryption-key change shows "Your Twitch connection has expired" (`ConnectController.kt:878`). Expired, denied and failed device codes share one message. Every load, returning ones included, holds the splash for at least 1.2 s (`App.kt:59`).

Change:

| Cause | Message | Action |
| --- | --- | --- |
| Server unreachable | "Can't reach your bot server at bot.example.com." | Retry; on desktop also Use a different server; always Sign out |
| Encryption key changed | "Your bot's saved sign-ins were reset because its encryption key changed." | Reconnect Twitch, then Reconnect bot account |
| Device code expired | "That code expired after 10 minutes." | Get a new code |
| Device code denied on Twitch | "Twitch says access was declined." | Try again |

- A returning user with a valid session sees the shell at once with skeleton content, no minimum splash time.
- The "Backend URL" field shows only in the desktop app, never on the web (`ConnectScreen.kt:210-217`).

### F8. A viewer's first visit is about them (P11)

Today: a pure viewer sees an empty channel switcher and "You're not connected to any channel yet" (`strings.xml:19`). Participant home calls a Moderator-only stats endpoint and shows the 403 text.

Change: the viewer home lists every channel on this instance where the viewer has history. One card per channel shows the balance (with the currency name), watch streak, rank on the leaderboard, and current song request, if any. Below the cards come the cross-channel settings: TTS voice, pronouns, linked platforms, and data and privacy. A viewer with no history sees "Chat in any channel that uses NomNomzBot and it will show up here" with a list of channels that are live now.

## Dashboard home and navigation

The home page looks the same live and offline, and the sidebar shows a broadcaster 45 pages in 9 groups, all expanded. The changes below give the home page two modes and cut what each persona sees to the pages they use. Paths are relative to `app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/`.

### N1. Persona-shaped sidebar (P8)

Today: every page the role can read is listed, 45 for a broadcaster, 37 for a moderator (`feature/shell/nav/ShellNav.kt:113-202`). Groups with more than two pages start expanded (`feature/shell/ui/ShellScreen.kt:859-873`). Chat holds 12 unrelated pages, including Overlays, Pipelines and Code Scripts.

Change:

- Add a **Pinned** group at the top with at most 8 pages. The defaults depend on the role:
  - Broadcaster: Dashboard, Chat, Commands, Event Responses, Overlays, Music, Moderation, Settings.
  - Moderator: Dashboard, Chat, Moderation, Review Queue, Viewers, History, Song Requests, TTS.
  - Editor: Dashboard, Commands, Event Responses, Timers, Overlays, Pipelines, Sound Clips, Chat.
- Every nav item gets a pin/unpin control in its right-click menu and its overflow menu. The pinned set is saved per user per channel.
- Regroup the rest by job. A page belongs to the group that names the job it serves, not where it was built.
  - Chat: Chat, Multi-Chat, Commands, Chat Triggers, Voice Triggers, Timers, Quotes, Random Responses.
  - Stream: Overlays, Event Responses, Schedule, Analytics, OBS, VTube Studio.
  - Automate: Pipelines, Code Scripts, Custom Events, Webhooks, Automation, Federation.
  - Moderation, Loyalty, Audio, Community and Setup stay as they are.
- Merge "Alerts & Events" into Event Responses as a filter chip **On-stream alerts**. The glossary already defines an alert as the output of an event response. Two entries for one concept force the user to guess which one holds the thing they want.
- Every group starts collapsed except Pinned and the group holding the active page. The expanded state is saved per user. Setup opens in one click (fixes the two-click toggle at `ShellScreen.kt:882-885`).
- No two pages share an icon (today ChatTriggers/EventResponses, Timers/Schedule and MultiChat/Community do, `ShellScreen.kt:1521-1548`).

Done when a fresh broadcaster sees at most 8 pinned items plus collapsed group headers above the fold at 1080 px height, and a moderator never sees Followers, Subscribers or Donations tiles.

### N2. Command palette (P4, P9)

Today: there is no search and no keyboard shortcut anywhere in the shell.

Change: Ctrl+K (Cmd+K on macOS) opens a palette from any page. It also opens from a search field at the top of the sidebar.

- It searches five kinds of result: pages, actions, viewers, commands and channels.
- Actions take arguments inline: `timeout <name> 10m`, `ban <name>`, `title <text>`, `ad 60`, `clip`, `marker`, `skip song`, `raid <channel>`.
- When the query is empty, it lists the last 5 used items first.
- Each result row shows its shortcut, so users learn the shortcut by using the palette.
- An action that needs a role the user lacks is listed greyed with "Needs Editor" instead of hidden. Users then learn why they cannot do it.

Done when every live action in the H3 table can be run from the palette without touching the mouse.

### N3. Queue badges on nav items (P7, P10)

Today: nav items carry no counts (`ShellScreen.kt:1130-1185`), so a growing queue is invisible until someone opens the page.

Change: show a numeric badge on Queue (M6, which absorbs held AutoMod messages, TTS approvals, media share, unban requests and reports) and on Song Requests. The count comes from the same live push that updates the page.

- The badge is neutral grey while the oldest item is under 60 seconds old.
- It turns accent once the oldest item passes 60 seconds. Waiting viewers are the thing worth noticing.
- A collapsed group shows the sum of its children's badges on the group header.

### N4. Channel switching for moderators of many channels (P8, P9)

Today: the switcher is an unsorted dropdown. Switching replaces the whole shell with a splash (`ShellScreen.kt:296`). The header's live dot is hardcoded green (`ShellScreen.kt:951-959`). The channel is not in the URL.

Change:

- Sort the list as live channels first (by viewer count), then the rest alphabetically.
- Show a search field once the user has more than 5 channels.
- Ctrl+1 to Ctrl+9 jump to the first nine channels in that order.
- Switching keeps the sidebar and the current page. Only the content area shows a skeleton while it loads. If the target channel lacks the current page, open its Dashboard and show the toast "Commands isn't available on \<channel>".
- The header dot is red when the channel is live and grey when offline. It updates from `StreamStatusChanged`.
- Put the channel in the URL: `#/c/<channel-login>/<page>`. A link shared between two moderators then opens the same channel and page.
- Remove the duplicate identity block at the bottom of the sidebar. The profile menu moves into the channel header's dropdown.

### N5. No silent route fallback (P7)

Today: an unknown or forbidden route quietly lands on Dashboard (`ShellScreen.kt:344-357`).

Change: render the requested page's title with one line, "You need Editor or higher to open Pipelines. Ask the broadcaster for access.", and a **Back to Dashboard** button. Browser Back on the first shell page must not sign the user out. Instead it shows "Press Back again to sign out" for 3 seconds.

### H1. Two home layouts: Prep and Live (P1)

Today: the layout never changes. `StreamStatusChanged` only flips `isLive` (`feature/home/state/HomeController.kt:311-314`). After going live, uptime shows "—" and the viewer count stays frozen until a reload. Eight equal stat tiles sit above the live state (`HomeScreen.kt:405-407`).

Change: Home renders one of two layouts and swaps within 2 seconds of the stream state changing. Swapping needs no reload and has no animation beyond a 150 ms crossfade.

**Live layout**, top to bottom:

1. **Live strip**, one row. It holds:
   - a LIVE pill;
   - uptime, ticking every second on the client;
   - concurrent viewers in large type, with a small up or down delta against 5 minutes ago;
   - one chip per live platform with its own viewer count, so the D1 promise of one channel on many platforms is visible;
   - the next ad as a countdown ("Ad in 4:12") with an inline **Snooze** button. `nextAdAt` is loaded today but never rendered.
2. **Needs you now**: the attention inbox (H4), shown only when it holds items.
3. **Live actions**: one row of large buttons, in this order: Mark moment, Clip, Poll, Prediction, Run ad, Raid, Edit title. Each shows its keyboard letter (M, C, P, R, A, D, T).
4. **Active poll or prediction**, while one runs: live bars per option, time left, and an **End** button. This replaces the xs text line at `HomeScreen.kt:1188-1205` and the separate Chat poll card.
5. Two columns:
   - Left: the live activity feed. It updates on push, collapses identical consecutive events into one row ("Stoney\_Eagle redeemed TTS ×12, last 21:14"), and shows time of day, not date.
   - Right: Now playing, with title, requester and a **Skip** button, then Top commands this stream.
6. **This stream so far**, one quiet row: new followers, new subs, bits, chatters. These replace the 8 equal tiles.

**Prep layout**, top to bottom:

1. **Ready to go live**: a checklist that is computed, not ticked by hand. Each failing row carries a one-click **Fix** link to the exact control.
   - Bot is in chat on each connected platform.
   - Every enabled overlay has been loaded by OBS in the last 24 hours (from the widget heartbeat).
   - Spotify, Discord and TTS connections are healthy.
   - Title and category are set.
   - The bot account token is valid.
2. **Next stream**: title, category and tags, edited in place, plus up to 5 saved presets (title + category + tags) applied with one click.
3. **Last stream recap**: duration, peak viewers, new followers, new subs, clips made, top 5 chatters, and the top command.
4. **Get started** (H2), while unfinished.

Done when a test stream going live switches the layout within 2 seconds without a reload, uptime ticks, viewers update at least every 60 seconds, and nothing above the fold in Live layout needs reading more than 5 words to act on.

### H2. Get started checklist that shows progress (P2, P3)

Today: three rows ("Connect an integration", "Create your first command", "Build a pipeline") appear only while all three are empty, and the card vanishes as soon as one exists (`HomeController.kt:178-194`).

Change: the card shows 5 steps with a progress bar that starts at 1 of 5 because signing in counts as done. Starting a list already in progress makes people more likely to finish it.

1. Signed in.
2. See the bot answer in your chat (F3).
3. Add your alert overlay to OBS.
4. Turn on a follow alert.
5. Make your first custom command (seeded starter commands don't count).

Each step deep-links to the exact control, and each completes from real state, never from a click on the checklist. "Build a pipeline" leaves the list: it is an advanced tool, not a first step. The card can be dismissed with "Hide for now" and stays hidden for 7 days. It disappears for good at 5 of 5, after a one-time "You're set up" message.

### H3. Live actions: fewer clicks, honest availability, the right safety (P6, P9)

Today: a moderator sees 4 of 7 tiles greyed out. Raid, Ad and Clip are offered offline and then fail with a toast. Cancel Prediction refunds every bet with no confirm (`HomeScreen.kt:453,456`).

Change:

- Hide actions the user's role cannot perform. Greyed tiles teach nothing and take space on every visit. Show live-only actions only while live.
- Apply one safety rule across the whole app, defined in section Cross-cutting (X2):

| Action | Safety | Detail |
| --- | --- | --- |
| Edit title | Inline result | Dialog stays open until Twitch and Kick confirm; then the toast "Title updated on Twitch, Kick". On failure, the error shows inside the dialog with Retry. |
| Mark moment, Clip | None | Runs on press; the toast carries the clip link as a real link with Copy. |
| Start poll or prediction | None | Sent on submit. |
| End poll | Delayed send | "Ending poll in 5 s · Undo". |
| Cancel prediction | Confirm | "Refund 12,400 points to 37 viewers?" with the real numbers; the button reads **Refund and cancel**. |
| Run ad | Delayed send | Length picker defaults to the channel's last used length, not 30 s; then "Ad starts in 5 s · Undo". |
| Raid | None | The countdown then shows in the Live strip with Cancel raid until it fires. |

- Raid search accepts any channel by name. Before typing, it lists live channels the streamer follows, sorted by viewers. Today it only searches the channel's own past chatters (`HomeController.kt:255-266`).
- Tags in Edit title become chips with autocomplete. Free comma-separated text goes.

### H4. One attention system, two placements (P10)

Today: the sidebar trigger and the Home inbox each fetch attention separately (`ShellScreen.kt:404-411`, `HomeController.kt:167`), so dismissing in one leaves the other stale.

Change: one shared store feeds both placements, and a dismiss in either updates both at once. Each item carries one of three severities, and the severity alone decides how loud it is:

- **Critical** (only while live): the bot is out of chat on any platform, an enabled overlay disconnected, or the bot token died. It shows a toast that stays until dismissed and plays an optional sound, off by default.
- **Action** (held AutoMod messages, queues past 60 s): badge and inbox row only.
- **Info** (an integration needs reconnecting while offline, an update is available): inbox row only, never a badge.

The Reauth dialog must not open as a blocking modal while the stream is live (`ReauthDialog.kt:44-53`). Live, it becomes a Critical inbox item with **Reconnect Twitch**. Offline, the modal stays.

### H5. A home for moderators (P1, P8)

Today: moderators get the streamer's home, with follower, sub and donation tiles and greyed actions.

Change: a user whose role on the active channel is below Editor gets **Mod home**.

1. **Live now**: every channel they moderate that is live, one row each with viewers, held AutoMod count, review-queue count, and an **Open chat** button.
2. The active channel's held messages and queues.
3. Their own last 10 mod actions across channels, each with Undo where the action allows it.

The hub must stay subscribed to queue counts for every live moderated channel, not only the active one, so the counts are live.

## Commands, event responses and pipelines

The seven authoring editors (Commands, Event Responses, Alerts, Chat Triggers, Timers, Random Responses, Quotes) each save, validate and insert variables differently. Only one, the built-in reply editor, shows the chat line the user is writing. Every editor closes before the server answers, so a failed save loses the form. A click on the dimmed background throws away a whole form without asking. The changes below give every editor one contract and put the chat line in front of the user while they write.

### A1. One editor contract (P5, P6, P7)

Today:

- Primary buttons are a text button in Commands, Alerts, Triggers and Timers, but a filled button in Event Responses.
- Create is labelled "Create" in three editors and "Add" in three others.
- Alerts shows no toast on success (`AlertsController.kt:191`).
- `Dialog` closes on a scrim click (`core/designsystem/.../Dialog.kt:74`), and no editor tracks unsaved changes.

Change: every authoring editor follows these rules.

1. **One filled primary button**, bottom right. It reads "Create command" for a new item and "Save changes" for an edit. The noun changes per editor; the verbs never do.
2. **The editor stays open until the server answers.** While waiting, the button shows a spinner and the form is read-only. On success it closes and the toast reads "Command !socials created · Undo". On failure it stays open, the input is kept, and the error appears above the button with **Try again**.
3. **Leaving a changed form asks first.** A scrim click, Escape, browser Back or a sidebar click on a form with changes opens "Discard your changes to !socials?" with **Keep editing** (primary) and **Discard**. An unchanged form closes without asking.
4. **A disabled primary always says why.** Under the button, one muted line names what is missing: "Add a name and a response to create this command."
5. **Errors appear after the user leaves a field, never on open.** Today the Commands name field is red the moment the dialog opens (`CommandsScreen.kt:859`).
6. **Undo instead of "can't be undone".** Deleting a command, timer, trigger, quote or random list deletes it at once and shows a toast for 10 seconds: "Deleted !socials · Undo". If anything depends on the item (a pipeline, a timer, a reward), a dialog lists those dependents first, using the existing blast-radius dialog. Merge the two duplicate `BlastRadiusLoadState` types into one.
7. **Same words for the same thing:** "Who can use it" everywhere (not "Who can trigger it"), and "Pipeline" everywhere (not "Bound pipeline" or "Run pipeline (optional)").

Done when a test that fails the save request leaves every editor open with its input intact, and a scrim click on a changed form always prompts.

### A2. "What chat will see" beside every response (P5)

Today: only `BuiltinRepliesDialog.kt:243-268` renders a live preview. Commands, Event Responses, Alerts, Chat Triggers and Timers show raw template text only.

Change: every field that produces a chat line gets a preview panel directly under it, titled **Chat will see**.

- It renders the template through the same resolver the bot uses, with sample data:
  - a real recent chatter's name from this channel (falling back to "Ana" for a channel with no chat yet);
  - sample args ("!so Ana");
  - real values where they exist (uptime, current game, follower count).
- It applies the D5 line prefix and the channel's tone, and shows whose account the line will come from: "NomNomzBot:" or "Stoney\_Eagle: \*".
- A line longer than 500 characters shows where it will split: "Sends as 2 messages".
- With random responses on, the panel shows every variant stacked, each labelled "1 of 3", and so on.
- An unknown variable is underlined in red in both the field and the preview, with "Unknown variable {usr}. Did you mean {user}?" and a one-click fix.
- A secondary **Send test to chat** button sends the rendered line once, prefixed "\[test\]", without saving.

Done when no editor can save a template that contains an unknown variable without a visible warning.

### A3. Variables are picked, never remembered (P4)

Today:

- The helper link is deliberately muted (`TemplateHelpersDialog.kt:74-77`).
- It appends the token at the end of the text instead of at the cursor. Commands adds no space, Event Responses adds a smart space, and Timers always lands in the last message row (`TimersScreen.kt:694-718`).
- Chat Triggers hard-codes "Reply (uses {user}, {args})" in its label.
- Pipeline help text teaches `{{user.name}}` (strings at S:2970, 3064, 5005, 5007, 5017, 5195), but the resolver only reads single braces (`TemplateResolver.cs:1377`). A user who follows the help sends the raw text to chat.

Change:

- Typing `{` in any template field opens an autocomplete at the cursor. It lists only the variables valid in that context (for example, the event type for event responses). Each row shows the name, one line of meaning and the sample value: `{user}`, "Who ran the command", "Ana".
- The muted link becomes a visible **Insert variable** button beside every template field, including each random-response row and each timer message row. It inserts at the cursor of the focused field, adding a space only when the neighbouring character is not already a space or punctuation.
- Correct every `{{ }}` help string to single braces.
- The same picker serves Commands, Event Responses, Chat Triggers, Timers, Random Responses and pipeline step fields. Retire the hard-coded label.

### A4. Commands: write the reply first, decide the machinery later (P3, P8)

Today: the dialog asks **Command type** (Text response / Pipeline / Code script) as its third field, before anything is written (`CommandsScreen.kt:888-905`). It always shows 12 or more controls, with no advanced section. Cooldown defaults to 0.

Change: the form order becomes:

1. **Command**: the name, with the channel's prefix shown as a fixed adornment (`!`), so users type `socials`, not `!socials`.
2. **Response**: the text, with the A2 preview and a **Random responses** segmented control ("One reply" / "Pick one at random").
3. One compact row with **Who can use it** (default Everyone) and **Cooldown** (default 5 s per viewer, 0 s for the channel). A per-viewer default stops one viewer from spamming without blocking everyone else.
4. **More options**, collapsed: aliases, prefix override, match mode and pattern, channel-wide cooldown, enabled on create.

Type choice leaves the top of the form.

- Under Response, a link reads "Needs logic, sounds or overlays? Turn this into a pipeline." Clicking it creates a pipeline whose first step sends the text already written, binds it to the command, and opens it in the pipeline editor. Nothing typed is lost.
- "Run a code script instead" sits inside More options.
- Aliases get the same collision check as names. An alias that matches a built-in or another command shows the existing shadow warning (`CommandsScreen.kt:784-794`).

The empty Commands page shows 6 starter cards instead of "No commands yet": `!socials`, `!discord`, `!schedule`, `!lurk`, `!so`, `!rules`. Each opens the form pre-filled, with the preview already rendered. When a search finds nothing, the page says "No command matches 'xyz'" with **Create !xyz**, not the empty-state text.

### A5. Event Responses absorbs Alerts and speaks in plain events (P4, P8)

Today: Alerts and Event Responses edit the same event-keyed rows with different powers. Creating an alert takes the event type as free text, so the user must know raw keys like `channel.subscription.gift` (`AlertsScreen.kt:517-523`).

Change:

- Remove the Alerts page; its route redirects to Event Responses with the **On-stream alerts** filter on (N1).
- Each Event Responses row is one sentence in plain words: "When someone follows", "When someone gifts subs", "When a raid arrives". No raw keys anywhere in the UI.
- Each row's right side shows its outputs as chips: Chat, Overlay, Sound, TTS, Pipeline. Off outputs appear as outline chips. The row also shows when it last fired ("2 h ago").
- **Test** on a row fires a sample event through the real path. The chat line goes to the A2 preview only, unless the user picks **Also send to chat**. The overlay test goes only to the alert widget, not every subscribed widget. Today it fans out to the whole channel (`WidgetsController.kt:284-287`).

### A6. Triggers and timers that read as sentences (P5, P7)

Today:

- The pipeline switch in Chat Triggers is disabled when the channel has no pipelines (`ChatTriggersScreen.kt:532-537`), and Timers hides its picker in the same case (`TimersScreen.kt:722`).
- An out-of-range timer interval silently disables Save.
- A timer opened for editing re-seeds its fields when the detail finishes loading (`TimersScreen.kt:617-632`), overwriting anything typed in the meantime.

Change:

- Under each trigger and timer form, show a one-sentence summary that updates as fields change, for example:
  - "When anyone says *discord* (any case), the bot replies once every 30 s at most."
  - "Every 30 minutes, if at least 5 chat messages arrived since the last one, the bot types one of 3 messages in order." Numbers in a sentence are read correctly far more often than numbers in separate fields.
- The pipeline option is always visible. With zero pipelines it offers **Create a pipeline** inline, as Commands already does through `PipelineBindPicker`.
- An out-of-range interval shows "Pick between 1 and 1,440 minutes" under the field.
- The edit form shows a skeleton until the detail loads, and only then becomes editable.
- Each timer row shows its next fire time live: "Next in 12 min", or "Waiting for 3 more chat messages".

### A7. Pipelines: one draft, tested as written (P5, P6)

Today:

- Create makes an empty graph and returns to the list, so the user must find it and open it (`PipelinesController.kt:179-188`).
- A step's "Save" only edits memory, while "Save chain" persists. **Back** discards silently (`PipelinesController.kt:351-355`).
- Removing a block deletes its whole subtree immediately.
- **Test** runs the last saved version, not the draft on screen (`PipelinesController.kt:796-800`). Its sample variables are typed as `key=value` lines.
- A hub `pipelines` change reloads the list and can throw the user out of the editor (`PipelinesController.kt:138-145`).
- Six equal buttons (Step, If, Switch, Loop, Random, Try) compete in the header.

Change:

- **New pipeline** opens a picker with 6 templates ("Shoutout when a raid arrives", "Sound and chat line on a reward", "Welcome first-time chatters", "Song request with a cost", "Timed giveaway reminder", "Clip on a command") plus **Blank**. It then opens the editor directly.
- **One save level.** Step dialogs apply to the draft with **Apply**. The editor header shows an "Unsaved changes" chip and one filled **Save pipeline** button. The draft is kept in local storage after every change, so a reload offers "Restore your unsaved draft from 21:14?". Back with unsaved changes uses the A1 prompt.
- Removing a block happens at once, with "Removed If block and 3 steps inside it · Undo". Ctrl+Z and Ctrl+Shift+Z step through the draft's history.
- **Test** runs the draft on screen. Sample inputs become a form generated from the trigger's variables, pre-filled with realistic values the user can change. The result shows the chat lines in the A2 style, then each effect in order with its timing.
- A save by someone else while the editor is open shows a banner, "Ana saved a newer version of this pipeline 1 min ago", with **Load theirs** and **Keep mine**. The editor is never replaced underneath the user.
- Replace the six header buttons with one **Add step** button. It opens a searchable palette grouped into Chat, Media, Moderation, Economy, Integrations and Logic. If, Switch, Loop, Random and Try live under Logic.

### A8. Smaller fixes in the same area

- Code Scripts says "Versioned Lua scripts" (S:1920) but runs JavaScript on Jint. Change the copy to JavaScript. New scripts start from a template picker ("Reply with a random fact", "Call a web API", "Keep a counter"), not an empty textarea.
- Rename "Pick Lists" to **Random Responses** in every label, dialog and doc, matching the nav. The token `{list.pick.<name>}` stays.
- Quotes: Game becomes a category picker that defaults to the current game. The form shows the `!quote` line it will produce, through A2.

## Overlays and OBS

Getting an alert onto the stream means working in two apps at once with no guidance on the OBS side. Today a streamer collects up to four kinds of OBS URL from four pages: one per widget on Overlays, the bridge on OBS, the TTS overlay on TTS, and VTube Studio (whose bridge URL lives on a separate server page). The changes below make OBS setup a guided loop that ends in a visible test alert.

### O1. Install ends inside OBS, not on a list (P2, P7)

Today: installing from the gallery closes the dialog and reloads the list, with no message and no next step (`WidgetsController.kt:366-372`). The row then offers a URL with a Copy chip, and nothing explains what to do with it in OBS.

Change: after **Install**, a panel opens on the new widget, **Add to OBS**.

1. **When the OBS connection is set up:** one primary button, **Add to my current scene**. It creates the browser source through obs-websocket (`CreateInput`) at the widget's declared size and names it "NomNomzBot · Follow alert".
2. **Otherwise:** the URL with **Copy**, the size to enter ("Width 1920, Height 1080", from the widget manifest) and three numbered steps with OBS's own words: "In OBS, click + under Sources", "Choose Browser", "Paste the URL and set the size above".
3. **In both cases:** a live line, "Waiting for OBS to load this widget…", turns into "Loaded in OBS" from the presence signal the row already uses. **Send a test alert** then becomes the primary button.

The panel closes only when the user closes it. Get started step 3 completes on the first presence signal, not on Install.

### O2. One place for every OBS source (P4, P8)

Today: OBS URLs live on Overlays, OBS, TTS and an unbuilt VTube Studio view, in two sidebar groups.

Change: the top of Overlays gets an **OBS sources** panel. It lists every browser source this channel needs: each enabled widget, the TTS overlay, and the OBS bridge if the bridge mode is on. Each row shows a status ("Loaded in OBS 2 min ago", "Never loaded", "Last seen 3 days ago") and **Copy**, and with an OBS connection also **Add to scene**. The TTS and OBS pages link to this panel instead of showing their own URLs.

### O3. Token rotation never blanks the stream by surprise (P6)

Today: the per-widget **Rotate URL** keeps the old URL working for 15 minutes and shows old and new side by side (good). The channel-wide **Rotate token** swaps immediately with no grace period (`server/.../ChannelService.cs:697-716`), and leaves the user to re-copy every row by hand.

Change:

- The channel-wide rotate also keeps the old token valid for 15 minutes.
- Afterwards it shows the OBS sources panel (O2) in "update" mode, with a countdown, "Old URLs stop working in 14:32", and each row marked "Updated" as OBS loads the new URL.
- With an OBS connection, a primary **Update all sources in OBS** rewrites every source's URL through `SetInputSettings`.
- The confirm dialog states the count: "Rotate the token for all 7 OBS sources?"

### O4. Widget rows show status and the two things people do (P8)

Today: each row carries up to 10 controls (Settings, Edit code, Test, Update or Reset, Versions, Rename, Clone, Rotate URL, Delete, toggle) (`WidgetsScreen.kt:1040-1180`). **Settings** only exists for system widgets, so gallery widgets can only be configured by editing code.

Change:

- A row shows the name, a status chip (Loaded in OBS / Not in OBS), the enable toggle, **Test** and **Configure**. Everything else moves to a row overflow menu.
- **Configure** opens a form generated from the widget's declared settings for every widget, gallery widgets included. **Edit code** is reached only through **Clone to edit**, so the installed widget always stays updatable.
- Words follow the glossary: the page and nav say **Overlays**; each item is a **widget**; the header button reads **Add widget**, and its dialog "Add a widget". Retire "Create Overlay" and "Create Overlay Widget".
- Test alerts use a real recent viewer's name and plausible numbers. Today a test renders "Someone" and "0 viewers", which looks broken on stream.

### O5. OBS page: connect first, controls second (P8, P6)

Today: 14 cards on one scroll, from Connection to "Raw request batch" and "Vendor request". **Stop streaming**, **Stop recording** and **Rotate bridge token** fire with no confirmation.

Change:

- Three tabs: **Connection** (default until connected), **Live control** (scenes, stream and record, audio mixer, studio mode; default once connected) and **Advanced** (filters, source actions, hotkeys, screenshot, raw and vendor requests).
- **Stop streaming** confirms with the real consequence: "End the stream for 1,234 viewers?" and the button **End stream**. **Stop recording** uses the 5-second delayed send with Undo (X2).
- **Rotate bridge token** gets the same 15-minute grace and update flow as O3.
- The connection hint shows the OBS menu path as numbered steps, and the password field has a **Paste** button.

## Moderation and live-stream flows

A moderator cannot see who someone is from the chat. Names are not clickable, and warnings and notes exist only behind a banned-user row. The feed scrolls to the newest line on every message, so during a raid the line being acted on moves away. Most actions give no visible result. Confirmation weight does not match the stakes: a delete and a ban across every moderated channel both cost 3 clicks, while Ban on a held message fires in 1 click with no confirm. The changes below put context and reversibility at the point of decision.

### M1. A user card one click from any name (P4, P9)

Today: no user card exists in Chat or Multi-Chat (`ChatScreen.kt:427-476`). Looking someone up means leaving the chat for Viewers. The profile there offers Ban and Unban only (`ViewerProfileScreen.kt:489-501`). Warn, notes and standing are reachable only from a banned-user row (`ModerationScreen.kt:2120,2425-2470`).

Change: clicking any username, in Chat, Multi-Chat, a queue row, the mod log or the activity feed, opens a **user card** as a side panel. The chat keeps running beside it. The card shows, top to bottom:

1. Display name, pronouns and every linked platform identity as icons (one human, D1).
2. Account age, follow age, first seen here, messages here, and watch streak.
3. Standing, trust and heat as named levels, never numbers.
4. Shared mod notes from one store, merging today's two (ledger L193), each with author and date.
5. Their last 10 messages in this channel.
6. Mod history: every timeout, ban, warning and deletion, each with who, why and when.
7. Actions:
   - **Timeout**, with presets 1 m, 10 m, 1 h and 1 d, plus a custom duration;
   - **Ban**;
   - **Warn**;
   - **Add note**;
   - **Unban** or **Remove timeout** when one applies.

Warnings and notes work for anyone, not only banned users. Done when every number in the card is reachable without leaving the chat page.

### M2. Act at the speed of chat (P9)

Today: there are no moderation shortcuts anywhere. Timeout from chat is always 600 s, with no reason (`ChatController.kt:315`).

Change:

- Hovering or keyboard-focusing a chat line shows three icons beside the existing "…" button: Delete, Timeout and Ban. The "…" stays, so touch users and anyone who does not hover keep every action.
- The Timeout icon's dropdown lists the presets. A single click uses the channel's default duration, 10 m unless changed in Enforcement Rules.
- **Keyboard mode**:
  - J and K select the previous and next line. Selecting pauses auto-scroll (M3).
  - D deletes, T opens the timeout presets (1 to 4 pick one), B bans, W warns, U opens the user card.
  - Escape returns to live scroll.
  - Pressing ? lists all shortcuts.
- Reason chips (Spam, Bot, Harassment, Hate, Off-topic) sit under the ban and timeout controls. One click fills the reason; typing stays possible.

### M3. The feed holds still while you aim (P9)

Today: both feeds jump to the newest line on every message, unconditionally (`ChatScreen.kt:323-325`, `MultiChatScreen.kt:384-386`).

Change: auto-scroll pauses while any of these is true:

- the pointer is over the feed;
- the user has scrolled up;
- a line's menu or the user card is open;
- keyboard mode has a line selected.

While paused, a pill at the bottom counts new lines, "27 new messages", and clicking it resumes. Auto-scroll resumes on its own 3 seconds after the pointer leaves the feed, unless the user scrolled up.

### M4. The line shows what happened to it (P7, P6)

Today: after a delete or timeout the whole feed reloads (`ChatController.kt:309-312`). After a ban nothing visible happens, and the network-ban result is ignored (`ChatController.kt:325-331`). The toast API has no action slot (`core/feedback/FeedbackMessage.kt`).

Change:

- No feed reload after an action. The acted-on line changes in place.
- A deleted message becomes "Message deleted by Ana", shown to moderators only, with the original text revealed on click.
- A timed-out or banned user's lines turn muted and carry a tag: "Timed out 10 m by Ana · Undo" or "Banned by Ana · Undo". Undo is available for 10 seconds. It runs the real inverse (remove timeout, unban) and the tag updates to "Undone by Ana".
- Every other moderator's dashboard shows the same tag from the hub `ModAction` push. Two moderators then never act twice on the same person.

### M5. Confirmation matches the blast radius (P6)

Today: three different weights for the same action. The Chat ban confirms. The held-message Ban and Block term fire in 1 click (`AttentionInbox.kt:438-444`). A ban across every moderated channel costs the same 3 clicks as a local one.

Change: apply the X2 safety ladder.

| Action | Safety |
| --- | --- |
| Delete message, timeout, warn | None; result shown in place (M4) |
| Ban in this channel, from anywhere (chat, card, held message) | None; 10-second Undo |
| Block term from a held message | None; Undo, and the toast says "Also blocks 3 held messages with this term" |
| Ban across all linked platforms of one person (D1) | Checkbox in the ban control, on by default, listing the platforms: "Twitch, Kick" |
| Ban in every channel I moderate | Confirm, with a required reason: "Ban Ana in 14 channels?" Then the toast "Banned in 13 of 14 channels · 1 failed" with details |
| Clear chat | Confirm: "Clear all messages for everyone in chat?" Then a success toast (silent today) |
| Nuke | Confirm with typed match term, and a Revert button in the result toast and on the History row for 24 hours |

Moderation calls carry the platform of the line they came from. Today they carry only a Twitch user id (`ChatApi.kt:52,59-62`), so an action on a Kick or YouTube line can target the wrong account.

### M6. One queue for everything waiting on a moderator (P10, P11)

Today: held AutoMod messages are spread over three surfaces (the Home dialog, the Queue page and the attention badge), each showing different context. The TTS queue sits in the fifth tab of the TTS page, loads once and never refreshes (`TtsQueueController.kt:20-24`). Its entries expire after about 10 minutes, with no countdown. Media Share ignores new submissions until a mod acts. The attention badge counts AutoMod holds only.

Change: Review Queue becomes **Queue**, the single place for everything waiting on a human.

- It holds held messages, TTS awaiting approval, media share submissions, unban requests, viewer reports and song requests flagged by trust rules.
- One list, oldest first, with filter chips per kind. Each chip shows its count.
- Every row uses the same component:
  - the user's name, opening the user card;
  - trust and heat;
  - age ("held 2 m");
  - an expiry countdown when one exists ("expires in 6:12");
  - the content;
  - inline actions.
- The Home dialog renders these same rows.
- TTS rows show the original text to moderators, with the censored parts highlighted, not only the censored version (`TtsScreen.kt:895`).
- Keyboard: J and K move, A approves, D denies, and U opens the user card.
- Everything updates from hub pushes; nothing loads once.
- An empty queue says "All clear" with the time of the last change, so an empty list cannot be mistaken for a failed load.
- A **Channel** filter offers "All channels I moderate", so a moderator of many channels works one list.

### M7. A spam wave has a one-screen response (P9, P10)

Today: Shield mode is one click on Chat (good). Clear Chat sits on the Moderation desk only. Nothing in the feed can act on "everyone who sent this".

Change:

- When the message rate passes 3 times the stream's 10-minute average, or more than 20 first-time chatters arrive within 1 minute, a banner appears over the chat: "Chat is spiking." It offers **Shield mode**, **Followers-only 10 m**, **Slow 30 s** and **Dismiss**. Each button shows its on state once active ("Slow · 30 s").
- A chat line's menu adds **Delete all like this** (same normalized text, last 2 minutes) and **Time out everyone who sent this**. Each shows the count before running ("Time out 18 users for 10 m?"), then reports the result with Undo.
- Ctrl+Shift+S toggles Shield mode from any page.
- Chat mode pills show a filled state when on, with the value inline, per the Sleak review: "Slow · 30 s", "Followers · 10 m".

### M8. Song request moderation acts on the request, not its position (P6)

Today: Remove, Ban track and Skip act on a queue position (`SongRequestsController.kt:198,207`). A queue that shifts while a confirm dialog is open can remove the wrong song. There is no way to stop one viewer from requesting.

Change:

- Actions target the request id.
- Remove uses the 10-second Undo instead of a confirm.
- Each row's menu adds **Block Ana from song requests** (for this stream, or permanently).
- The requester's name opens the user card.

## Integrations, settings and configure-once pages

Broken connections fail where nobody looks. A Spotify 401 reads as "nothing playing". Every expired card says "Kick chat is paused", whatever the provider. Discord has no real test post. Settings is one non-scrolling page of 10 cards, with Delete channel beside Join channel. The changes below give each connection a provable health state, and give Settings a shape that keeps danger separate.

### I1. Every connection proves itself (P7)

Today:

- `IntegrationCard` uses one string for every provider: "Reconnect needed — Kick chat is paused" (`IntegrationsScreen.kt:676-683`, S:1359).
- There is no Test button on any integration.
- The "expired" status is never written, so the shell badge and the Music page error rarely fire (ledger L10, shortcomings A2).

Change:

- Each card shows one of three states, named for its own provider: **Connected** / **Needs reconnecting** / **Not connected**.
- Under the state sits the last successful use, in plain words: "Last played a song 3 min ago", "Last posted to #live 2 days ago".
- A **Test** button does one real, harmless round trip and reports the result on the card:
  - Spotify reads the player state: "Connected · Playing on Desktop".
  - Discord posts "Test from NomNomzBot" to the chosen channel and links to it.
  - TTS synthesizes and plays "This is a test" in the default voice.
  - YouTube reads the channel name.
- Any 401 or 403 from a provider writes **Needs reconnecting** at once, and raises an attention item (H4): Critical while live if a live feature depends on it (song requests, TTS), Info otherwise.

### I2. Viewers never see the streamer's problem (P11)

Today: when Spotify is disconnected, a viewer's `!sr` gets "Couldn't queue … the music connection needs to be reconnected." A disabled game tells viewers to "turn it on under Economy → Games" (`ToneTemplateCatalog.Core.cs:281`).

Change: every chat line answers the viewer's situation, not the owner's. Owner text goes to the attention inbox.

- Song requests with a broken provider: the viewer sees "Song requests are paused right now." The streamer gets the I1 attention item.
- A disabled game: the viewer sees "!slots isn't on in this channel." Owner setup copy only appears in the dashboard.

### I3. Settings in tabs, with danger kept apart (P8, P6)

Today: 10 cards in one column that does not scroll, so the lower cards are cut off (`SettingsScreen.kt:314-379`, ledger VERIFIED). Delete channel and Reset configuration sit in "Channel management" next to Join channel. Language and timezone are free text ("BCP-47 code", "IANA name").

Change:

- Settings becomes 6 tabs, and the page scrolls:

| Tab | Holds |
| --- | --- |
| General | Command prefix, bot line marker (D5) with a rendered chat line, language (dropdown of shipped locales), timezone (searchable list, detected zone first), auto-join chat |
| Bot voice | Personality tone, engagement responses, streak milestones, greet cooldown |
| Bot account | The dedicated bot account (moved from "White-label bot" and Integrations' "Bot account") |
| Permissions | The single Twitch permission view (F6) |
| Billing | Plans, usage, limits, invoices, invite code |
| Data and danger | Event journal export and import, then a separate red-bordered **Danger zone** card holding Leave channel, Reset configuration and Delete channel |

- Stream info leaves Settings: title, category and tags live in the Home Prep layout (H1).
- Appearance (emoji style) applies to this device only, so it moves to the profile menu under "On this device".
- **Delete channel** needs the channel name typed. **Reset configuration** lists what it resets, with counts ("14 commands, 6 timers, 9 event responses").
- Changing the tone keeps its existing confirm, and adds three sample lines in the old and new tone side by side.
- "Rebuild projections" is renamed "Rebuild statistics from history", with one sentence on when to use it.

### I4. Discord announcements in one guided pass (P2, P5)

Today: three gates in two places (connect on Integrations, "Approve consent" with a typed "Discord user ID", then a master switch). Preview renders only.

Change: one flow on the Discord page, each step unlocking the next:

1. Connect a server.
2. Pick the announcement channel.
3. Write the go-live message with the A2 preview, rendered as a Discord embed.
4. **Send test post**, which posts for real and links to the message.
5. **Turn on announcements**.

Consent is taken from the OAuth identity of whoever connected the server. No ID is typed.

### I5. Disconnects and other one-way doors (P6)

Today: the Bot account Disconnect, Channel Points "Take control" and Bundles "Overwrite" run without a confirmation.

Change:

- Every disconnect uses the existing counted blast-radius dialog. The bot account version names the visible consequence: "The bot will type as Stoney\_Eagle with the \* marker until you connect another bot account."
- Take control states what changes on Twitch.
- A Bundles import with Overwrite lists the items it will replace before running.

### I6. Names that match across pages (P12)

- The page header "Rewards" becomes **Channel Points**, matching the nav.
- The public song request link lives only on Song Requests, called **Public request page**. Music links there. Rotating it keeps the old link working for 15 minutes, as widget URLs do. (The page itself must first be served: today `/sr/{token}` falls through to the dashboard, ledger L3.)
- Roles and permits: rename "Permits" to **Individual permissions**, with the subtitle "Give one person one extra ability without changing their role." Show action keys as grouped plain labels ("Moderation · Ban users"), not `moderation:ban` (`RolesScreen.kt:898`). Keep one way to grant a single ability; remove the duplicate member-level "Grant".
- Features rows show a plain name and one line on what the feature does. The raw `featureKey` and scope strings go behind "Technical details" (F6).
- TTS: entering a BYOK key asks "Use this key for TTS now?" and sets the dispatch mode and provider in one step. Today that takes two settings on two tabs. Per-viewer voice picks the viewer by name search, not by "Viewer Twitch user ID". Moderators land on the TTS page's Queue tab when items are waiting.

### I7. Economy is set up before it is operated (P2, P3)

Today: the Economy page opens on a config card, then leaderboards, accounts and holders. **Earning rules**, the step that makes the economy do anything, sits below the operational views. Watch-time earning is seeded disabled.

Change:

- While the economy is off, the page shows a three-step setup:
  1. Name your currency, singular and plural, with a live sample: "1,240 Nomz".
  2. Choose how viewers earn, with watch time pre-selected at a suggested rate and chat activity as an option.
  3. Choose what they can spend on (games, store, song requests).
- **Turn on economy** finishes it, and only then do the operational views appear.
- Once on, the currency name appears in every chat line that mentions an amount (V5).

## Viewers in chat

A viewer judges the bot in about two seconds: it either answers or it seems broken. Today the bot is silent in exactly the moments a viewer most needs an answer: entering a giveaway, winning one, joining a heist, redeeming a reward that fails, buying from the store. When it does answer, it often leaves out the one fact that would stop the viewer retrying, such as how long the cooldown has left or who may use a command. Built-in cooldowns are channel-wide, so the second viewer to type `!sr` is told they are on cooldown for something they never did (`ChatMessageHandler.cs:286-289`). Each retry earns another bot reply. The changes below make every attempt end in a short, specific answer, without flooding chat.

### V1. Every attempt gets an answer that stops the retry (P7, P11)

| Situation | Today | Change to |
| --- | --- | --- |
| On cooldown | "That command is still on cooldown." | "!sr is ready again in 8 s." (`GetRemainingCooldown` already exists) |
| Not allowed | "You don't have permission to use that command." Sassy adds "Nice try, though." | "!so is for moderators." Names the lowest role that can use it; the tone may decorate but never drops the role |
| Near-miss typo | Silence | "Did you mean !socials?" only when one enabled command is 1 edit away; at most once per viewer per 10 minutes |
| Unknown command, no near match | Silence | Silence (unchanged; replying to every unknown word is noise) |
| Game off | Owner text: "turn it on under Economy → Games" | "!slots isn't on in this channel." (I2) |
| Bet out of range | "Bet is outside the allowed range." | "Bets go from 10 to 500 Nomz. You have 240." |
| Not enough currency | "Insufficient funds." | "That costs 300 Nomz. You have 240." |

Change the cooldown model too:

- Built-in cooldowns become **per viewer** by default, matching A4's default for custom commands. The channel-wide cooldown stays available as an option.
- A viewer gets at most one cooldown notice per command per cooldown window. Further attempts in the same window are ignored silently.

### V2. Song requests show your place in line (P11)

Today: an accepted `!sr` gives the track and link, with no position or wait (`SongRequestBuiltin.cs:114`). Nothing tells a requester when their song starts. A duplicate request reply always blames "someone".

Change:

- Accepted: "Added Song by Artist. You're #4, about 12 min." The wait is the sum of the remaining track lengths ahead.
- **Now playing for you**, on by default: "@Ana your request Song is playing now." Two requesters in a row are merged into one line.
- A duplicate names the original requester's display name.
- `!queue` shows the viewer's own position first ("You're #4 with Song"), then the next 3 songs.
- The "lost at the provider" notice becomes a normal, re-wordable, toned reply slot. Today it is a hard-coded English line (`SongRequestLostAtProviderChatNotice.cs:38`).

### V3. Giveaways and live games happen in the open (P11)

Today: opening a giveaway is not announced. Entering is silent, and so is a rejected entry. When winners are drawn, nobody in chat is told (`GiveawayDrawnEvent` has no consumer). A winner claims by chatting, and gets no reply (`GiveawayKeywordListener.cs:108-137`). Heist joins and results show only on the overlay.

Change:

- **Open**: "Giveaway open: type !join to enter. Followers only, ends in 5 min."
- **Entries are confirmed in batches**, not one reply each, so a busy giveaway cannot flood chat: every 15 seconds, while new entries arrive, the bot types "Ana, Bo and 12 others are in. 54 entries so far." Seeing the count grow shows the giveaway is real and busy, which draws more entries.
- **A rejected entry** gets one reply per viewer per giveaway: "You need to follow to enter." The current "deliberately silent" rule exists to avoid flooding; the per-viewer limit solves that without leaving the viewer guessing.
- **Draw**: "Winner: @Ana, picked from 54 entries. Ana, type anything in chat within 60 s to claim." The draw method (uniform random, or weighted by tickets) is stated on the giveaway's public page.
- **Claim**: "Ana claimed the prize. Congrats!" No claim in time gives "No claim from Ana. Drawing again…", then the next draw.
- **Live games**: joins use the same 15-second batch line, a full round says "The heist is full (12/12)" once, and the result gets one summary line in chat as well as the overlay.

### V4. Points are never taken for nothing (P11)

Today: a redemption for a reward that is disabled locally does nothing, though Twitch has already taken the points (`RewardRedeemedHandler.cs:84-92`). A failing pipeline is only logged. Store purchases take currency and run nothing.

Change:

- When a redemption cannot run (reward off locally, pipeline failure, missing integration), cancel the redemption on Twitch so the points go back, and reply: "Your points are back. That reward isn't working right now."
- Store purchases refund automatically on failure, with the same reply.
- Each failure also raises an attention item for the streamer, naming the reward.

### V5. Viewers can find what exists and see their standing (P4, P11)

Today: `!commands` lists every enabled trigger as a flat, comma-separated line with no descriptions, wrapping across messages (`CommandsBuiltin.cs:85-180`). There is no `!points` or `!balance`. `!leaderboard` shows raw Twitch ids instead of names (`EconomyLeaderboardService.cs:146-154`). `!mydata` points to a page viewers cannot open.

Change:

- `!commands` replies with the 8 most-used commands the viewer can run, then a link: "All commands: nomnomz.bot/c/stoney\_eagle". The link opens a public commands page listing each command with its description, who can use it, and its cooldown.
- Add a built-in `!points` (alias `!balance`): "@Ana you have 1,240 Nomz, #12 in this channel."
- `!leaderboard` shows display names.
- `!mydata` links to the viewer home's data page (F8), which a viewer can open.
- Every chat line about an amount uses the currency name: "You won 120 Nomz on slots. Balance: 1,360 Nomz."

### V6. The bot never spams and never answers late (P10)

Today: every send waits in one 20-messages-per-30-seconds bucket (`TokenBucketChatSendQueue.cs:11`), and nothing is ever dropped. During a raid or a follow burst, notices and thank-yous queue up, and replies to commands arrive long after the viewer asked.

Change: every outgoing line gets a priority.

| Priority | Lines | When the queue backs up |
| --- | --- | --- |
| 1 | Direct replies to a command | Always sent |
| 2 | Thank-yous and alerts | Merged during bursts: "Thanks for the follows, Ana, Bo and 6 others!" |
| 3 | Notices (cooldown, permission, typo hint) | Dropped when the oldest queued line is more than 5 s old |
| 4 | Timers and announcements | Skipped for this cycle |

Any line still unsent 30 seconds after its trigger is dropped, not sent. A late answer reads as the bot misfiring.

### V7. Regulars feel noticed (P11)

Today: follows, subs and raids get personal thanks (good). First-time chatter, returning chatter and watch streak responses all start off.

Change:

- First-time chatter welcome and watch streak milestones are offered pre-checked during setup (F5).
- Greetings are limited to one per viewer per stream.
- The thank-you for a sub anniversary names the months ("6 months, Ana!"), which already exists in the resub line.
- Every recognition line can be re-worded per tone, like the other reply slots.

## TTS end to end

With default settings, a new channel's TTS is on, open to everyone, unmoderated and allows 500 characters, yet it makes no sound on stream. The default plane WAS `client_edge` (`Domain/Tts/Entities/TtsConfig.cs:40`), which spoke through the browser's `speechSynthesis`; the overlay SDK itself said OBS does not capture that audio (`OverlaySdkController.cs:329-331`) while the dashboard reported "Test sent to the overlay". Decided 2026-10-05 (owner: "edge tts is available everywhere and must be the standard. that lame outdated browser native tts goes away"): the standard is bot-synthesized Edge audio (`self_host`), the browser voice is gone from the app and the specs, and a stored `client_edge` reads as `self_host`. Around that sit four more gaps:

- No test in the dashboard plays audio. The synthesized `audioBase64` is never read (`core/network/TtsApi.kt:428`).
- Nothing triggers TTS until the streamer hand-builds a pipeline with a `play_tts` step and the undocumented `{input}` variable.
- There is no fast way to silence it, and moderators cannot use its playback controls at all.
- A viewer whose message is rejected, expires or fails is never told, and never gets points back.

The changes below make TTS heard, safe by default, stoppable in one press, and honest with the viewer who paid for it. Server paths are relative to `server/src/`, dashboard paths to `app/.../dashboard/`.

### T1. The default setup is heard on stream (P2, P7)

Today: see above. Edge and Azure catch their own errors and return empty audio, so the Edge fallback in `TtsService.cs:76-99` never runs, and a failure ends as "The TTS provider returned no audio."

Change:

- A new channel's first TTS line must play inside the OBS browser source as an audio element. Today only server-synthesized audio does that. So the default plane becomes server-side synthesis with the free Edge voices.
- This reverses `tts.md` decision 3 (zero server cost), so it needs the owner's call. If cost rules it out, the onboarding (T2) must say plainly that TTS is off-air until a provider is set.
- An empty result from any provider falls through to the next one: the BYOK provider, then free Edge voices, then failure. Only after all of them fail does T6's failure path run.
- Rename the "Dispatch mode" options to **Where voices come from**: "Free voices (recommended)", "My own provider key" and "My own TTS server". The control moves to the Provider tab (T11).

Done when a fresh channel that adds the TTS source to OBS and presses **Send a test line** hears it in an OBS recording.

### T2. Turning TTS on is a four-step guided flow (P2, P3)

Today: TTS is "on" by default, but nothing triggers it. A reward that reads the viewer's message takes about 12 steps across Rewards and Pipelines. The variable `{input}` (`Rewards/EventHandlers/RewardRedeemedHandler.cs:72`) appears nowhere in the dashboard.

Change: new channels start with TTS **off**. The TTS page, while off, shows **Set up text to speech**, four steps on one screen.

1. **What can trigger it.** Checkboxes, with the first pre-checked:
   - "A channel point reward". This creates a reward called "Text to speech" costing 500 points, with viewer text required and a bound pipeline that speaks "{user} says: {input}".
   - "Cheers of at least 100 bits".
   - "Sub and resub messages".
   - "A !tts command for subscribers". The streamer never sees a pipeline unless they open it.
2. **The channel voice.** A short list of 6 recommended voices in the channel's language. Each has a play button that speaks "Thanks for the follow, Ana!" in that voice, in the browser. One is pre-selected.
3. **How careful to be.** Three presets, with the middle one pre-selected:

| Preset | Who | Length | Approval | Always on |
| --- | --- | --- | --- | --- |
| Relaxed | Anyone who triggers it | 300 characters | Never | Profanity filter, link and emote removal |
| Balanced (recommended) | Anyone who triggers it | 200 characters | Only for viewers first seen in the last 7 days | Same |
| Strict | Anyone who triggers it | 150 characters | Every message | Same |

4. **Add it to OBS.** The O1 panel for the TTS source, ending on **Send a test line**. It plays through OBS and shows "Played on 1 OBS source" from the overlay's acknowledgement, not "sent".

**Turn on TTS** completes the flow. Every value stays editable afterwards on the Rules tab. A preset never hides its numbers; the table above is shown under the choice.

### T3. Hear every voice before it airs (P5)

Today:

- **Preview** only plays a voice that has a `previewUrl`, and only ElevenLabs provides one (`ElevenLabsTtsProvider.cs:163`).
- **Play** in Test voice prints `Synthesised via %1$s (%2$d ms)` and plays nothing.
- Test voice is hidden while Default voice is blank (`TtsScreen.kt:2423`), which is the default.
- **Use** on the Voices tab only fills a field on the General tab, and nothing saves until the user goes there and presses Save.

Change:

- Every play button produces sound in the dashboard: it plays the `previewUrl` if present, otherwise synthesizes a sample line and plays the returned audio.
- Test voice is always visible and uses the effective voice, including the platform default, named on screen: "Platform default: Jenny (en-US)".
- **Use** sets the channel voice immediately, with the toast "Channel voice is now Jenny · Undo".
- The raw voice-id text field is replaced by a searchable picker. Each row shows name, language, gender and a play button.

### T4. One press silences TTS, for moderators too (P9, P6)

Today: there is no stop control on Home. Skip, Pause and Clear sit on the fifth tab and need Editor (`TtsScreen.kt:528,799`). They are sent only to the caption widget, not the audio widget the setup recommends (`TtsConfigController.cs:173-189`). Turning TTS off does not clear what is already queued.

Change:

- A **Silence TTS** control appears in three places: the Live strip on Home (H1), the TTS page header and the palette (N2). Moderators can use it.
- One press does three things: stops the line playing now, holds the queue, and pauses new requests. The control then reads "TTS paused · 9:41 · Resume". The pause lasts 10 minutes by default; a small menu offers "Until I resume".
- While paused, new requests are kept in order. On resume they play in order. Any held longer than 10 minutes are refunded with T6's message.
- **Clear queue** refunds every paid item in it, after a confirm that names the count: "Clear 6 messages and refund 3,000 points?"
- Skip, Pause, Resume and Clear reach every playing TTS widget (the audio page; there is no browser voice).
- Moderators can Skip, Pause and Clear. Today these need Editor.

### T5. Clean the text before it is spoken (P5, P11)

Today: no URL, emote or cheermote stripping exists (inf., `EdgeTtsProvider.cs:538` only escapes SSML). Cheer messages include raw `Cheer500` tokens (`CheerEventHandler.cs:45`). An over-length message is rejected outright (`TtsDispatchService.cs:142-157`). "Read usernames" and "Skip bot messages" are saved but never read during dispatch.

Change: before the lexicon runs, every viewer text passes these rules in order:

1. Cheermotes and emote codes are removed.
2. URLs become the word "link".
3. Any character repeated more than 3 times collapses to 3 ("heyyyyyy" becomes "heyyy").
4. An all-caps message is lower-cased, so it is not shouted.
5. **Read usernames** prefixes "Ana says:" when on.
6. **Skip bot messages** drops lines from known bots when on.

A message over the limit is cut at the last whole word before the limit and ended with "…", not rejected. The viewer has usually paid, and hearing most of the message beats hearing none. The approval queue and the caption both show the cleaned text, so moderators approve exactly what will be heard.

### T6. The viewer always hears back, and never loses points (P7, P11)

Today: reward TTS is silent whether accepted, queued, rejected or failed. A custom `!tts` failure says only "Sorry, that command hit a snag and didn't finish." (`ChatMessageHandler.cs:799-810`). The specific reason, such as "That message is too long to read out", reaches only the pipeline log. Rejected items have no redemption id, so they cannot be refunded.

Change: every request carries its source (a redemption id, cheer or command) to the end, and each outcome sends one chat line at priority 1 (V6):

| Outcome | Chat line | Points |
| --- | --- | --- |
| Queued for approval | "@Ana your message is waiting for a moderator (2 ahead)." | Held |
| Played | Nothing; hearing it is the answer | Spent |
| Rejected by a moderator | "@Ana a moderator didn't approve your message. Your 500 points are back." | Refunded |
| Not reviewed within 10 min | "@Ana your message wasn't reviewed in time. Your 500 points are back." | Refunded |
| Couldn't be spoken (provider, no OBS source) | "@Ana TTS isn't working right now. Your 500 points are back." | Refunded |
| `!tts` too long (free command) | "@Ana that's 240 characters; the limit is 200." | Not applicable |

Refunds cancel the Twitch redemption (or reverse the currency spend). Each failure also raises an attention item for the streamer.

### T7. A fair approval queue (P10, P11)

Today:

- When approval is on, every request is queued, including the bot's own event-response lines, shoutouts and overlay tests (`TtsDispatchService.cs:211-219`).
- The queue shows newest first (`:357`), loads once, and caps at 25.
- `ExpiresAt` is set but never enforced, so a message can be approved hours later.
- Two moderators approving at once plays the line twice (inf.).

Change:

- Only viewer-written text can need approval. Bot lines skip the queue.
- The queue lives in M6. It is ordered oldest first, pushed live, has no 25-row cap, and every row shows its expiry countdown.
- Expiry is enforced by the server: at 10 minutes the item leaves the queue and T6's refund runs.
- The first moderator action wins. A second moderator sees the row change to "Approved by Bo" and cannot act on it again.
- Approve has a second option: **Approve and trust Ana for this stream**. Ana's later messages that stream skip approval. This cuts repeat work without lowering the bar for strangers.
- Approved items play in approval order, after anything already playing.

### T8. Gates read real viewer data (P7)

Today:

- Reward pipelines never pass the viewer's role or bits, so a subscriber redeeming on a "Subscribers" channel is refused as a stranger.
- Cheer events set `bits`, not `user.bits` (`CheerEventHandler.cs:43`), so a bits threshold blocks cheer TTS.
- Quotes and scripts send "everyone" with 0 bits (`QuoteBuiltin.cs:182-183`), so a bits or role gate silences the bot's own lines.
- "Also read the reply out with TTS" on built-in commands only works for `!quote`.

Change:

- Every TTS request carries the triggering viewer's real standing and bits.
- Bot-originated lines bypass the who-can and bits gates entirely.
- The built-in toggle appears only on built-ins that honour it, until each built-in supports it.

### T9. One clean playback on stream (P7)

Today:

- Both `tts_audio` and the seeded `tts_caption` autoplay `tts_speak`, so with both loaded the stream hears an echo (inf.).
- TTS overlaps alert sounds; nothing ducks or waits.
- The caption names the speaker by raw platform user id (`tts_caption.vue:35`).
- With no source connected, the audio is lost, and the dispatch is still recorded as a success.

Change:

- Only the audio widget plays sound. The caption shows text only, timed from the audio's actual start.
- TTS waits for an alert sound in progress to finish, and alerts wait for TTS. One sound at a time. An optional "Lower Spotify volume while TTS plays" setting exists, off by default.
- The caption shows the display name.
- If no TTS source is connected while live, a request waits up to 2 minutes for one. Meanwhile a Critical attention item says "TTS has nowhere to play. Add the TTS source in OBS." After 2 minutes the request is refunded through T6.
- If a viewer's saved voice is no longer in the catalogue, the channel voice is used. The viewer is told once: "@Ana your voice Ana (Neural) isn't available anymore, so I used the channel voice. Pick a new one with !voice."
- `!voice roulette` picks only from voices in the channel's language.
- When self-service voices are turned off, `!voice clear` still works.

### T10. Viewers choose their voice in the dashboard (P11)

Today: the only way is `!voice` in chat. The `me/voice` API exists but nothing calls it (`core/network/TtsApi.kt:125-131`), against D4.

Change: the viewer home (F8) gets a **My TTS voice** card. It has the T3 searchable picker with play buttons, plus a choice between "Use everywhere" and "Only in this channel". It shows "Channel default" when nothing is set, and offers **Reset to default**.

### T11. A TTS page shaped by job (P1, P8)

Today: five tabs (General, Voices, Per-viewer, Pronunciation, Queue & test). The page always opens on General. Picking a voice spans two tabs, and BYOK spans two tabs with three saves.

Change: four tabs.

| Tab | Holds | Opens by default when |
| --- | --- | --- |
| Live | Now playing with Skip, the queue (M6 rows), Silence TTS, and Send a test line | Live, items are waiting, or the viewer is a moderator |
| Voice | Channel voice (T3), per-viewer overrides (picked by name, picker above the Assign button), pronunciation rules | Offline, for Editors and up |
| Rules | Every source that can trigger TTS (rewards, bits threshold, sub messages, commands, event responses with TTS on), each linked to its editor; the safety preset and its numbers | Never by default |
| Provider | Where voices come from (T1). A provider key saved here asks "Use this key for TTS now?" and sets the source and provider in one step | Never by default |

- Remove key confirms: "Remove your Azure key? TTS falls back to free voices."
- "Browser-source URL" becomes "TTS source for OBS", listed in the O2 panel.
- The pipeline step "Read aloud (TTS)" lists `{input}` in its variable picker as "Viewer's message".
- Its help text uses single braces, not `{{user.name}}` (S:5017).

## Cross-cutting rules

These rules apply to every page, including the ones this spec does not name. Each section above points back to them.

### X1. Feedback timing

- A control shows its pressed state within 100 ms. The result, or a progress indicator, appears within 1 second.
- Only the control that was used shows progress: a spinner in the button or row, never a full-page spinner for a single action.
- The toast gets an action slot. Today `FeedbackMessage` holds only kind, label and args (`core/feedback/FeedbackMessage.kt`).
  - Success toasts last 4 s, or 10 s when they carry Undo.
  - Error toasts stay until dismissed and carry Retry when a retry makes sense.
- **Dialogs never close before the result.** This covers Home's title, prediction, raid, commercial and resolve dialogs (`HomeScreen.kt:523-581`), the Roles dialogs (`RolesScreen.kt:343-389`) and every editor (A1).

### X2. The safety ladder

Every action sits on exactly one step, chosen by what can be reversed and how many people it reaches. A confirmation dialog that appears on routine actions trains people to click through it, so confirms are kept for the top two steps.

| Step | When | Behaviour | Examples |
| --- | --- | --- | --- |
| 0. None | Creating, toggling, sending | Runs at once | Create command, toggle a timer, start a poll |
| 1. Undo | The app can run an exact inverse | Runs at once; toast with Undo for 10 s | Delete a config item, timeout, ban in one channel, remove a pipeline block |
| 2. Delayed send | External, cannot be reversed, low stakes | Waits 5 s with a visible countdown and Undo, then sends | End poll, run ad, stop recording |
| 3. Confirm | Cannot be reversed, reaches many people | Dialog with the real numbers; the button names the act | Cancel prediction (refunds), ban in all my channels, clear chat, end stream, rotate the channel token, disconnect an integration, reset configuration |
| 4. Type to confirm | Destroys data | Dialog where the user types the name | Delete channel, erase a viewer's data, nuke |

Confirm buttons never read "OK", "Yes" or "Confirm". They name the act: "Refund and cancel", "End stream", "Delete channel".

### X3. Empty, loading and error states

- **Empty, never used:** what the page is for, one primary action, and starter templates where they exist (A4, A7).
- **Empty because of a search or filter:** "No results for 'xyz'", with a clear-filter link.
- **Queue empty:** "All clear", with the time of the last change (M6).
- **Loading:** a skeleton shaped like the content. Never a blank page, and never a form that becomes editable before its data arrives (A6).
- **Failed load:** "Couldn't load commands" with Retry, never an empty list. Today several pages show a failed load as empty, which reads as "nothing here".

### X4. Errors in human words

Every error says what happened, why if known, and what to do next. Never show:

- raw server messages ("Action failed: %1$s", `strings.xml:20`);
- HTTP status text as page content;
- an empty 403. The server returns the action, the role required and the role held, so the client can say "You need Editor to change timers."

### X5. No raw identifiers in the interface

People appear as display names with a platform icon, never as numeric ids or GUIDs. Wherever a field asks for an id today, it becomes a search-by-name picker:

- the per-viewer TTS voice;
- Discord consent;
- the economy transfer recipient;
- adding a moderator;
- mod log rows;
- the nuke confirmation.

Internal keys (`featureKey`, action keys, event types, scopes) appear only behind "Technical details".

### X6. Live data is pushed

Every number that can change while the page is open updates from the hub. Pages that load once today get push updates:

- live-ops state;
- uptime and viewers (H1);
- the TTS queue;
- media share;
- participant balances and song queue.

Polling (the chat poll card's 4-second loop) is replaced by the same pushes.

### X7. One word per concept

Follow the glossary in `PRODUCT-ALIGNMENT.md` everywhere: overlay and widget, event response and alert, the bot types (chat) and speaks (TTS). Labels start with the verb ("Add widget", "Send test post"). Every user-facing string goes through the string resources, so Dutch and English stay at parity. Bot chat replies use the channel's language once the composer takes a language parameter (ledger L3 `I18N`).

### X8. Keyboard first, mouse always

Every action is reachable by keyboard. The palette (N2) lists every shortcut beside its action, and ? opens a shortcut sheet on any page. Nothing is hover-only: anything revealed on hover also appears on keyboard focus and under the row's "…" menu.

## Implementation order and how to know it worked

The order puts trust first: stop losing people's work and points before teaching them anything new. Items marked **new** add a capability that does not exist today. Under D8 (stabilize before adding) they need the owner's go-ahead or a place in the tracker as ideas. Every other item changes how an existing feature behaves.

| Phase | Items | Why in this position |
| --- | --- | --- |
| 1. Stop silent loss | A1, X1, X4, I1, I2, V1, V4, F7, M5 | Lost forms, lost points and misleading errors cost trust that no new feature wins back. Mostly small, contained fixes. |
| 2. First value | F1, F2, F3, F4, F5, F6, H2, O1 (**new**: O1's add-to-scene) | Every new channel passes through this path once; it decides whether they stay. |
| 3. Live mode | H1 (**new**: stream presets), H3, H4, M3, M4, X2, X6, V6 | Live is where mistakes are public and attention is scarcest. |
| 4. Moderator speed | M1, M2 (**new**: keyboard mode), M6, M7 (**new**: spike banner), M8, N3, N4, H5 | Persona 2 in the product statement, with almost no dedicated surface today. |
| 5. Authoring confidence | A2, A3, A4 (**new**: starter cards), A5, A6, A7 (**new**: templates), A8 | Preview and undo remove the fear of breaking the stream, which is what stops people building. |
| 6. Viewer loops | V2 (**new**: now-playing notice), V3, V5 (**new**: `!points`, public commands page), V7, F8 | Viewer delight compounds once the dashboard side is stable. |
| 7. Consolidation | N1, N2 (**new**: palette), N5, O2, O3, O4, O5, I3, I4, I5, I6, I7, X3, X5, X7, X8 | Reshaping navigation and settings is safest once the pages it rearranges behave well. |

TTS items slot into the same phases: T1, T5, T6 and T8 join phase 1, because TTS is silent on stream and keeps points it should return. T2 and T3 join phase 2, T4 and T9 phase 3, T7 phase 4, T10 phase 6 and T11 phase 7.

### Measures

| Measure | Today | Target |
| --- | --- | --- |
| First click to a confirmed bot reply in the streamer's chat | SaaS about 30 s with no confirmation; self-host 5 to 15 min | Under 2 min, confirmed on screen (F3) |
| Sidebar entries a new broadcaster sees before scrolling | 45 pages, groups expanded | 8 pinned plus collapsed group headers (N1) |
| Clicks from a chat line to a timeout of a chosen length | 4, fixed at 600 s | 1 click or 1 key (M2) |
| Editor saves that fail and lose the typed input | Every editor | 0 (A1) |
| Redemptions or purchases that take points and do nothing | Not counted; happens on any disabled reward | 0, each refunded (V4) |
| Cooldown and permission notices per 1,000 chat messages | Not counted | Count from phase 1, expect a drop after per-viewer cooldowns (V1) |
| New channels whose alert overlay loads in OBS within 7 days | Not counted | Count from phase 2; set a target after one month of data |
| Bot replies sent more than 30 s after their trigger | Not counted; nothing is ever dropped | 0 (V6) |
