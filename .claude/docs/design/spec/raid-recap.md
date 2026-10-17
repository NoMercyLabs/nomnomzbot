# Raid recap screen

Status: spec only. No code yet. Date: 2026-10-16. Owner: Stoney.

## 1. Goal

Stoney, verbatim: "feature: i want an ending raid screen with all the stats from the stream like subs."

When the streamer raids out, the stream shows a recap screen. It lists what happened this stream. Every number is real. The screen thanks people. It never asks anyone to subscribe.

Rules this spec honors:
- All data real. No seed or sample numbers in production.
- No nudge to subscribe, follow, or give. The screen only reports what already happened.
- No bully data. No "worst", "most banned", or "most timed out" lists. Only the streamer's own totals and thanks.
- Plain English labels, as translation keys. No text in code.

## 2. Benchmark

Three products read this run. Sources are the pages named in the search results. Page bodies were not opened in a browser; rows marked "snippet" come from a search result summary only and must be re-read in a real browser before slice S1 starts (not checked).

| Feature | Seen in | Source | Ours | Status | Slice |
|---|---|---|---|---|---|
| Rolling credits list of who supported the session | Streamlabs End Credits | streamlabs.com/stream-widgets/end-credits (snippet) | Recap grid, not a scroll | partial | S3 |
| Choose which groups appear (followers, subscribers, donors, mods, raids, cheers) | Streamlabs | streamlabs.com/content-hub/post/the-credits-widget (snippet) | Per-block toggles in widget settings | missing | S3 |
| Rename group labels (Bits as Cheers) | Streamlabs | same (snippet) | Labels are translation keys, not renamable yet | not-doing, v1 uses plain fixed labels | none |
| Delay, roll speed, roll time, loop | Streamlabs | same (snippet) | Hold time and fade settings | partial | S3 |
| Data is current session only | Streamlabs | same (snippet) | Stream window from `Stream.StartedAt` | have | S1 |
| Built-in Credits service, enable events | Streamer.bot | docs.streamer.bot/guide/core/credits (snippet) | Widget plus pipeline step | partial | S4 |
| Events: follows, cheers, subs, resubs, gift subs, gift bombs, raids, redemptions | Streamer.bot | same (snippet) | Ledger in section 3 | partial | S1 |
| Goal contributions, pyramids, hype trains | Streamer.bot | same (snippet) | Not recorded as stream totals | missing | later |
| Chatters filtered by role | Streamer.bot | same (snippet) | Chatter counts only; names need privacy review | missing | S2 |
| Track top (cheers, channel points used) | Streamer.bot | same (snippet) | Top cheerers widget exists | partial | S2 |
| Auto reset 12 h after close, manual reset, reset on stream online | Streamer.bot | same (snippet) | Window is the Stream row, no reset needed | have | S1 |
| Session data object in widget load | StreamElements | support.streamelements.com Widget Data Overview (snippet) | Hub event carries the full recap payload | have | S5 |
| Per-stream summary: duration, avg and peak viewers, followers gained | TwitchTracker, Twitch analytics | twitchtracker.com/boxbox/streams/34869569680 (snippet) | Duration and peak exist; average does not | partial | S1 |
| Subs and follows gained per stream | Vision Stats | docs.1up.vision/stats/stream-stats (snippet) | Counted from the journal in the stream window | partial | S1 |
| Trigger | All three | none show a raid trigger | Raid out pipeline | have, new for us | S4 |

Layout in the benchmark: all three show vertical lists by group. Our pick is a fixed grid of cards, because a raid hands off in seconds and a scroll would be cut off by the scene switch.

Timing in the benchmark: Streamlabs scrolls for a set roll time. We show the screen for the raid countdown window plus a short hold (default 20 seconds, setting).

A full benchmark file (25 rows) at `.claude/docs/design/benchmarks/raid-recap.md` is owed before S3 starts (not written here).

## 3. Stat ledger

"Exists" means a record or count exists today. "Per stream" means it can be cut to one stream window.

| Stat | Source file:line | Exists | Per stream |
|---|---|---|---|
| Stream start and end time | server/src/NomNomzBot.Domain/Stream/Entities/Stream.cs:45,47 | yes | yes |
| Stream duration | derived from the two lines above; live uptime in DashboardStatsDto.cs:78-79 | yes | yes |
| Peak viewers | Stream.cs:54 (folded by StreamStatusPollingService.cs:222) | yes | yes |
| Average viewers | none | no | no |
| Current viewers | DashboardStatsDto.cs:31-32 | yes | live only |
| New followers | event journal `FollowEvent`; daily total ChannelAnalyticsDaily.cs:28 | yes | derive from journal window |
| New subs, resubs, gift subs (count) | journal events; daily total ChannelAnalyticsDaily.cs:29; fold at ChannelAnalyticsDailyProjection.cs:120-126 (all three kinds add 1 to one number) | yes | derive; daily number mixes kinds |
| Split of new / resub / gift | journal types in TwitchChannelEventLogProjection.cs:51-53 | yes | derive from journal |
| Gift subs: gifter name and count | GiftSubscriptionEvent.cs:26,29,35,38,41 (anonymous flag at :38) | yes | derive from journal |
| Bits total | ChannelAnalyticsDaily.cs:30; fold at ChannelAnalyticsDailyProjection.cs:135-136; CheerEvent.cs:33 | yes | derive from journal |
| Top cheerers | CheerEvent.cs:27,30,33; widget top_cheerers.vue:10-19 (in-widget running total) | yes | derive from journal |
| Raids in (who, viewers) | RaidEvent.cs:21-30; log row TwitchChannelEventLogProjection.cs:56 | yes | derive from journal |
| Redemptions (count) | ChannelAnalyticsDaily.cs:32; Redemption.cs:30,33,38 (title, cost, time) | yes | yes (RedeemedAt window) |
| Songs played | TrackChangedEvent.cs:19-34; MusicHistoryReader.cs:34 (GetRecentAsync) | yes | derive from window (not checked: whether GetRecentAsync filters by time) |
| Songs requested | ChannelAnalyticsDaily.cs:33 | yes | daily only |
| Top song requesters | MusicHistoryReader.cs:84 | yes | not checked |
| Chatters (unique) | ChannelAnalyticsDaily.cs:25; ChannelChatterDay.cs:46 (LastStreamId) | yes | yes via LastStreamId |
| Chat messages | ChannelAnalyticsDaily.cs:26; DashboardStatsDto.cs:74-75 (session since bot join) | yes | derive from journal window |
| Top chatters (names) | WatchSession.cs:34 (MessageCountInSession, with StreamId at :29) | yes | yes |
| New chatters (first time in channel) | no stored flag; AccountRisk.cs:88 is a spam signal, not a stat | no | no |
| Commands run | ChannelAnalyticsDaily.cs:31; DashboardStatsDto.cs:70-71 | yes | derive |
| Games played | ChannelAnalyticsDaily.cs:36 | yes | daily only |
| Follower and subscriber totals now | DashboardStatsDto.cs:36,40 | yes | live read |

Gaps: average viewers, new chatters, an exact per-stream bits and subs ledger. Section 4 closes the first and third. New chatters stays out of v1 (not-doing, no stored flag).

Privacy: chatter names appear only for people who gave a public Twitch name in chat. Lists show thanks (gifters, cheerers, raiders, top chatters), never a moderation list.

## 4. Design

One system widget, `raid_recap`, shown full scene (1920x1080 browser source). Stat cards in a fixed grid, grouped by importance:

1. Headline row: duration, peak viewers, new followers, new subs (all kinds), bits.
2. Thanks row: gifters (name and count), top cheerers, raiders in, top chatters.
3. Channel row: redemptions, songs played, chat messages, unique chatters.

Rules for the card look: shadcn new-york tokens, concentric radius, one accent only on the headline row. No buttons (it is an overlay). The sleak skill is loaded before S3 (UI slice).

An empty card is hidden, not shown as zero with a made-up note. A stat the bot could not read shows a dash.

Timing: appears on the "Raid committed" signal. Holds `holdSeconds` (default 20, setting). Then fades. A new stream clears it.

Nothing on the screen asks to subscribe or follow.

### 4.1 Owner feedback on the preview (2026-10-16)

Stoney, verbatim: "Some small changes for the end credits like who subbed themselves, a top leader board for what commands and such bit it's nice. It does not need to be static, so use scrolling lists that repeat to your hearts content"

- Add a list of the people who subscribed themselves (not gifted), by name.
- Add leaderboards: the most-used commands, and similar top lists.
- The screen moves: long lists scroll and loop, end-credits style. The fixed grid above becomes the frame; lists inside it scroll and repeat.

Stoney, verbatim, on preview v2: "no, the person who gifted and the amount, and then each user who subscribed themselves. music is a top 5, not individual titles, just user. you are trying to fit too much into this page now". And: "please use CET" (shown as local Amsterdam time; CEST in summer).

- Subscriptions card: each gifter with their gift count, then each person who subscribed themselves. Gift recipients are not listed.
- Music card: top 5 requesters by request count. No song titles.
- Fewer blocks: one card per topic, no second list inside a card.
- A list only scrolls when it is longer than its card.

Stoney, verbatim: "can you make it a bit more on brand with my overlay?" and "can we pick a rainbow color for 31 days and pick the color of the current stream day?"

- The look copies the channel's own Leaderboard and Logo badge widgets: one hue `--base` with five shades (`--color-100` to `--color-900`), the 45-degree see-through card gradient with blur, the same font.
- The hue is fixed per stream, not animated: `(day - 1) * 360 / 31` degrees, where `day` is the day of the month the stream started (Amsterdam time). Day 1 is red; day 15 is green.

Stoney, verbatim, on preview v4 (avatar watermark, bottom-left of the Subscriptions card): "this needs to be more dynamic/fluid and react according to what happened every stream. i don't get 22 subs every stream. i like it but it may needs some fluid behavior. this also needs to be generated at the end of the stream live on my obs scene"

- Fluid layout: the grid is built from the blocks that have data this stream. Empty blocks are gone and the others grow to fill the space. A quiet stream (no subs, no raids) still fills the screen without gaps; a busy one scrolls its long lists.
- Card size follows content weight: a block with many rows gets more space than a block with one.
- The channel avatar is a faint watermark in the bottom-left of the first tall card.
Stoney, verbatim, right after: "i want to trigger this as my ending scene triggered by the !raid command"

- Trigger: the `!raid` command. Its raid commit pipeline (§7) shows the recap as the ending scene, built live from that stream's data. No stream-offline trigger.

## 5. API

New read endpoint, additive: `GET /api/v1/dashboard/{channelId}/stream-recap?streamId=` next to `DashboardController.cs:98` (`stats`). It returns `StreamRecapDto`. Default `streamId` is the latest stream, ended or live. The numbers are counted from the event journal and tables in the Stream window (`StartedAt` to `EndedAt`, or now). The journal has no stream id (EventJournal.cs fields at :27-77), so the window is by time.

DTO fields: `stream` (id, start, end, durationSeconds, peakViewers, title, game), `followers`, `subs` (`new`, `resub`, `gift`, `total`), `giftSubs` (list of gifter display name and count, anonymous merged as "Anonymous"), `bits` (total, top list), `raidsIn` (list), `redemptions` (count, top titles), `songs` (count, list), `chat` (messages, uniqueChatters, topChatters), `commandsRun`. Each list is capped (default 5).

Stoney, verbatim (2026-10-17): "all those hand made endpoints... can we not get something simpler like one endpoint that you can ask for any metric of today's stream?"

Decision: this replaces the single-purpose recap endpoint. One endpoint `GET /api/v1/dashboard/{channelId}/stream-stats?streamId=&metrics=a,b,c` (default: latest stream, all metrics). Each metric is one class implementing `IStreamMetric` (key, typed result, compute over the stream window), registered in DI. A new metric is a new class, never a new endpoint. The response maps each requested key to its typed result; OpenAPI describes every metric shape, so no client sees `unknown`. An unknown key is a 400 naming the key. The recap widget and the `show_raid_recap` action request the metric list from the widget settings.

Stoney, verbatim: "if we can fold this into the api and sdk as a single call with all the options that would be way cleaner than adding more and more endpoints and extra sdk calls"

- SDK: one call, `nnz.data.streamStats({ stream?, metrics })` for widgets and the same shape as `nnz.api.stats.stream(...)` for scripts, both generated in `SdkRuntimeSurface.cs`. The return type is `Pick<NnzStreamMetricMap, K>` over the requested keys, so a wrong key is a type error. A new metric extends the map; it never adds an SDK method.

Contract rule: land the Kotlin DTO and `server/openapi/v1.json` change in the same commit as the endpoint (CLAUDE.md, KMP contract).

## 6. Widget

- New file `server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/raid_recap.vue`, a compiled Vue SFC like `now_playing.vue` and `top_cheerers.vue`.
- New entry in `FirstPartyWidgetCatalogue.cs` and in `SystemSurfaceNaturalKeys` (channel-owned system surface, provisioned by `IWidgetService.EnsureSystemWidgetAsync`, WidgetService.cs:255). It is not a gallery item.
- Settings: `holdSeconds`, block toggles, `accentColor`. Defaults in the catalogue and the schema in `WidgetSettingsSchemaProvider.cs`.
- Data: the widget receives the full `StreamRecapDto` as the data of one hub event, `raid_recap.show`, sent through `IWidgetEventNotifier.SendWidgetEventAsync` (IWidgetEventNotifier.cs:20). It never computes numbers itself, so the numbers cannot drift from the API.
- Widget test samples in `WidgetTestSamples.cs` may carry fake data for the gallery preview only, as today (WidgetTestSamples.cs:40).

## 7. Pipeline step

The existing `widget_event` action (WidgetEventAction.cs:25-80) already pushes an event to a widget. It needs a `data` payload, and the payload must be built at run time. Plan: a new action `show_raid_recap` that builds `StreamRecapDto` for the channel's latest stream and sends `raid_recap.show` to the `raid_recap` system widget. It is fail-closed like `widget_event`: if the widget is not open in a browser source, it returns a typed failure and does not throw.

Seeder: `RaidCommitFlowSeeder.BuildSteps()` (RaidCommitFlowSeeder.cs:181-194) gets `show_raid_recap` as the FIRST step, before `obs_streaming stop`. Reason: the stop step ends the broadcast, so the recap must go out first. `ContinueOnError: true`, so a missing widget never blocks the stop or the raid message.

Existing channels: the seeder skips any channel already wired (RaidCommitFlowSeeder.cs:86-99). A new platform content version and a migration-free upgrade path adds the step to channels that still carry the untouched seeded pipeline only. A streamer's own edited pipeline is never touched. Because the seeder text and the platform payload share `BuildSteps()` (RaidCommitFlowSeeder.cs:156), one edit covers both.

Open point for Stoney (not blocking): a stream ending right after the raid may cut the screen. Hold time and the OBS stop timing are in one pipeline, so the streamer can add a wait step. v1 does not add one.

## 8. Slices

Each slice is one vertical piece with a test that can fail for the right reason.

- S1 Recap read model and endpoint. `StreamRecapDto`, a service that counts from the journal and tables in the stream window, controller action, OpenAPI snapshot, Kotlin DTO. Done when: a test seeds a channel with one stream and known events (2 new subs, 1 resub, one gift of 3 by a named gifter, 500 bits, 1 raid of 12, 4 redemptions) and the endpoint returns those exact numbers and shapes; an event outside the window is not counted; a stream with no events returns zeros and empty lists; `ApiContractTest` passes.
- S2 Missing stats. Add average viewers (fold in `StreamStatusPollingService` beside PeakViewers) and per-stream top chatters from `WatchSession`. Done when: tests prove the average over three samples and the ordered top-chatters list; a migration lands in BOTH sets (Postgres and SQLite) with a migration check.
- S3 The widget. `raid_recap.vue`, catalogue entry, system-surface key, settings schema, translation keys in English and Dutch, empty-block hiding. Done when: a Vitest mount test renders every block from a fixture DTO, hides empty blocks, shows a dash for a missing stat, contains no subscribe text, and honors `holdSeconds`; `compileKotlinWasmJs` not needed (server widget).
- S4 Pipeline action and seeder. `show_raid_recap` action, first step in `RaidCommitFlowSeeder`, platform content version bump. Done when: a test runs the action and asserts the exact `raid_recap.show` event and payload reach the notifier; the seeder test asserts the new step is first, and that a customized pipeline is left unchanged.
- S5 Live proof on the dev box. A throwaway channel row (never a real channel), one fake raid-out event through the real hub, a screenshot of the browser source. Done when: the screenshot shows the real numbers matching the endpoint; the throwaway row is deleted and the delete confirmed by a read.
- S6 Ending scene (decided 2026-10-16, §4.1): `!raid` shows the recap as the ending scene. Covered by S4; the open point in §7 (hold before the OBS stop) is answered by the streamer's own wait step.

Not checked in this run: whether `MusicHistoryReader.GetRecentAsync` takes a time window; the exact current widget event names in `NnzWidgetEventMap`; the Streamer.bot and StreamElements page bodies in a real browser.
