# Template helpers catalog (design, 2026-10-26)

Stoney, 2026-10-26: "go for it, you may make it crazy to 1000+ but they have to be useful and nicely organized".

This file is the design for 1000+ template helper forms. It holds no code. It is the input for the build lanes
(section 8). The 18 groups Stoney approved on 2026-10-26 are the scope; every row names a real data source that
was read in this session, or it is in the Cut table (section 7.20).

Status words used below:

- **as-built**: on `master` today (`TemplateHelperRegistry.cs`, `TemplateResolver.cs`), including the `{date:…}`
  expressions. They landed on `master` as commit `bf2487658` (`git log --oneline master -8` lists it second, after
  `e36a1513a`). Commit `e3fa37878` (the same change, from worktree `agent-a84138f1995d34e3f`) is not an ancestor of
  `master` (`git merge-base --is-ancestor e3fa37878 master` printed `NOT_ANCESTOR`), so the catalog names `bf2487658`.
- **not checked**: a fact this session did not read. It is checked before a lane builds on it.

## 1. Facts this design starts from (read this session)

| Fact | Where |
|---|---|
| The registry on `master` has 185 rows: 171 `Literal(`/`Prefixed(` rows plus 14 `DateExpression(` rows (the brief said 173). The command `grep -cE '^\s+(Literal\|Prefixed)\(' TemplateHelperRegistry.cs` prints `172`, because line 163 is the `DateExpression` helper's own `Prefixed(displayKey, …)` call, not a row; `grep -cE '^\s+DateExpression\(' …` prints `14`. | `server/src/NomNomzBot.Application/Abstractions/Templating/TemplateHelperRegistry.cs` |
| The resolver finds placeholders with `\{([^{}]+)\}`: one level, no nesting, no escape. | `TemplateResolver.cs:1687` `VariablePattern()` |
| `{list.pick.…}` and `{custom.…}` expand in pre-passes before the main pass. | `TemplateResolver.cs:117-134` |
| `{transform.<name>:<text>}` runs last, in a depth-bounded loop, so `{transform.upper:{user.name}}` works only because the inner key is replaced first. | `TemplateResolver.cs:167-170`, regex at `:1684` |
| `ExtractPlaceholders` collects the keys first; each group resolves only when `NeedsAny(needed, …)` is true. | `TemplateResolver.cs:140`, `:1365`, `:1373` |
| An unknown key renders as written. | `TemplateResolver.cs:96`, `:160` |
| Save-time validation uses the same one-level regex. | `TemplateHelperValidator.cs` `PlaceholderPattern()` |
| `TemplateHelperCoverageTests` scans the resolver source and the registry and fails when they differ. | `server/tests/NomNomzBot.Infrastructure.Tests/Platform/Templating/TemplateHelperCoverageTests.cs:228`, `:248` |
| Help text keys are `template.helper.<key>` in code and `template_helper_<key>` in `server/i18n/strings/values/strings.xml:5375` and `values-nl/strings.xml:5359`. | strings files |
| The i18n loader already reads several files per language (`strings_attention.xml`, `strings_builtin_replies_*.xml`). | `server/i18n/strings/values/` listing |
| The SDK has `format(template, values)` only, a plain `{name}` fill. There is no `nnz.template.render` today. | `server/src/NomNomzBot.Infrastructure/DevPlatform/SdkRuntimeSurface.cs:665-667` |
| The precedent for "one call, typed from a registry" is `streamStats`: `StreamMetricTypeScript` reflects every `IStreamMetric` class into `NnzStreamMetricMap`. | `server/src/NomNomzBot.Infrastructure/DevPlatform/StreamMetricTypeScript.cs` |
| `TemplatesController` serves `GET api/v1/templates/helpers`. | `server/src/NomNomzBot.Api/Controllers/V1/TemplatesController.cs:46` |
| Spec §6.3.2 already designs `if.*` (boolean path and value predicate, depth 8, about 4 KB output, cycle detection). It writes `{{…}}`; the as-built engine uses `{…}`. | `.claude/docs/design/spec/commands-pipelines.md` §6.3.2 |

## 2. Naming rules

1. One style: `{namespace.key}` reads a value. `{namespace.key:args}` calls a helper with arguments.
   A namespace may own one root form `{namespace:args}` (for example `{if:…}`, `{math:…}`, `{date:…}`).
2. Namespaces are lowercase, one word: `text`, `num`, `math`, `if`, `default`, `switch`, `chance`, `pick`, `set`,
   `var`, `include`, `stream`, `channel`, `user`, `target`, `viewer`, `count`, `list`, `music`, `chat`, `tts`,
   `platform`, `poll`, `prediction`, `hypetrain`, `goal`, `charity`, `ad`, `schedule`, `activity`, `emote`,
   `args`, `command`, `fun`, `lookup`, `bot`, `date`, `time`, `event`, `random`, `roll`, `quote`, `giveaway`,
   `currency`, `clip`, `link`.
3. Keys are `camelCase` after the first dot (`user.followAge`), matching the as-built keys. Lookup is not
   case-sensitive (as-built: `StringComparer.OrdinalIgnoreCase`). A capital first letter on a pronoun key
   capitalizes the value (as-built, `TemplateResolver.cs:161`); the rule extends to every text-valued key.
4. `user.*` is the person who triggered the template. `target.*` is the first `@mention` argument. `viewer.*`
   is a stats view of `user`, and `{viewer.x}` stays an alias of `{user.stats.x}`.
5. **Renamed keys are migrated, not kept.** Stoney chose on 2026-10-27: "A one-time database migration rewrites
   every saved template to the new names. No alias code stays." The migration `RewriteRetiredTemplateKeys` rewrites
   the retired keys in every saved template column; the registry holds canonical keys only.
6. Booleans are never printed (spec §6.3.2). A state such as `user.isSub` is a value for `if`, `switch` and
   conditions. The as-built `stream.isLive`, `target.known` and `tts.audioconnected` keep printing `true`/`false`
   as aliases, because templates use them today.

## 3. Argument syntax (shared by every helper)

| Rule | Form | Example |
|---|---|---|
| Key and arguments | the first `:` after the key starts the arguments | `{text.repeat:3\|ha}` |
| Argument separator | `\|` at brace depth 0 | `{if:{user.isSub}\|thanks\|sub for perks}` |
| Nesting | any `{…}` inside an argument is a full placeholder, resolved first (inside out) | `{text.upper:{user.name}}` |
| Lazy branches | `if`, `switch`, `default`, `chance` resolve only the branch they pick | `{if:{stream.isLive}\|{stream.uptime}\|offline}` |
| Optional arguments | `[…]` in the Name column; a missing argument takes the default named in the row | `{num.round:{x}}` = 0 decimals |
| Named arguments | `name=value` inside an argument, for helpers with more than 2 optional arguments | `{text.cut:{chat.message}\|max=200\|end=…}` |
| Escape | `\|` `\:` `\{` `\}` `\\` print the character | `{text.replace:a\|b\\|c}` |
| Literal braces | `{{` prints `{` and `}}` prints `}` (Stoney's group 16) | `{{not a key}}` prints `{not a key}` |
| Quoting | none. Spaces are kept as written, and leading or trailing spaces of an argument are trimmed. To keep them, use `\s` | `{text.pad.left:5\|7\|\s}` |
| Unknown or invalid | renders the token as written and logs one warning (as-built for unknown keys and for `date:`) | `{nope}` prints `{nope}` |
| Limits | nesting depth 8, output 4 KB per render, a cycle in `include`/`set` stops with the token as written | spec §6.3.2 |

**Parser change.** The one-level regex (`TemplateResolver.cs:1687`), the transform regex (`:1684`), the list-pick and
custom-data pre-passes and the validator regex are replaced by **one** tokenizer `TemplateParser` that builds a
small tree (`TextNode`, `HelperNode(key, args[])`). Resolution walks the tree inside out. The as-built
`{transform.x:…}` and `{verb:a|b}` shapes already fit the new rule, so they keep working. The as-built
`{random.pick.a.b.c}` (dots as separators) and `{roll.<name>.<min>.<max>}` keep their dotted form as aliases.

**OPEN DECISION D1 (Stoney): `{{` as a literal brace.** Today `{{user}}` renders `{ViewerOne}`, because the inner
`{user}` matches and the outer braces stay. Stored templates written in the old bot's `{{…}}` style may rely on that.
Making `{{` a literal changes them. Proposal: L1 counts stored templates that hold `{{` (commands, event responses,
timers, pipelines, Discord, webhooks) with one read-only query on the dev box and reports the count. Stoney picks:
(a) `{{` literal, with a one-time data pass that rewrites `{{key}}` to `{key}` per channel; or (b) the escape is
`\{` only and `{{key}}` keeps today's output. Until he picks, L1 builds (b) and `\{`.

## 4. Data-source declaration

Every registry row declares what it needs, so the resolver fetches only what the template uses (today:
`ExtractPlaceholders` plus `NeedsAny`, hand-written per group).

```
TemplateHelperEntry(
    Key,                // canonical key, e.g. "music.queue.position"
    Aliases,            // as-built keys that resolve here
    Arguments,          // typed list: name, kind (text|number|duration|key|list), optional, default
    Contexts,           // as-built TemplateHelperContext set
    EventScoped,        // as-built flag
    Source,             // TemplateDataSource: the provider's fetch group, e.g. MusicQueue, HelixHypeTrain
    Freshness,          // Pure | Seed | Memory | Cached(ttl) | Live
    Scope,              // Channel | Viewer | Target | Global
    Sample,             // fixed sample output (section 5), asserted by tests
    DescriptionKey)     // template_helper_<ns>_<key>
```

Resolution order per render:

1. Parse once into the tree.
2. Collect every `HelperNode` key (also inside branches, so the fetch is planned in one go).
3. Group the nodes by `Source`. Each provider's `PrepareAsync` runs once per source, with every node that needs it
   (one Helix call for `hypetrain.level` and `hypetrain.progress`).
4. `Pure` and `Seed` need no fetch. `Memory` reads `ChannelContext`. `Cached(ttl)` uses `HybridCache` keyed by
   channel and source. `Live` calls the API each render, and only `lookup.*` and `twitch.*` user lookups do that.
5. Sources run in parallel per render, each with the row's timeout; a failed source renders its fallback text.

## 5. Fixed sample data (every Output cell uses this)

Tests render each row against this fixture and assert the Output cell exactly. Random rows run with a seeded
`Random(1)` and the test asserts the value the seed gives; the Output cell shows the allowed set.

| Item | Value |
|---|---|
| Clock | 2026-10-26T14:30:00Z (Monday), channel zone Europe/Amsterdam (15:30 local), language `en` |
| Channel | login `samplechannel`, display `SampleChannel`, Twitch id `123456`, prefix `!` |
| Stream | live since 12:15:00Z (uptime 2h 15m), title `Building a bot`, game `Software and Game Development`, 42 viewers now, peak 57 |
| Last stream | ended 2026-10-24T22:00:00Z, game `Just Chatting` |
| User | `ViewerOne` (login `viewerone`, id `1001`), she/her, Tier 1 sub 7 months, follows since 2025-09-10, VIP no, mod no |
| Target | `ViewerTwo` (login `viewertwo`, id `1002`), he/him, follows since 2026-01-05 |
| Arguments | `!cmd @ViewerTwo hello big world` (4 arguments) |
| Counters | `deaths` = 12, `wins` = 3 (deaths this stream: 4) |
| Pick list | `snacks` = chips, cookies, fruit |
| Viewer stats | ViewerOne: watch 12h 30m, first seen 2025-09-10, 25 messages this stream, 1,204 in total, streak 5 streams, last redemption `Hydrate`, 2,500 points, rank 4, rank 3 has 2,800 |
| Music | now `Song A` by `Artist A`, 1:23 of 3:30, requested by ViewerTwo; 5 songs queued; ViewerOne is at place 3 with 2 songs, her next song starts in 7m 40s |
| Poll | `Best snack?`: chips 12 votes (60%), cookies 8 (40%), 1m 30s left |
| Prediction | `Win?`: Yes 3,000 points (75%), No 1,000 (25%), locks in 45s |
| Hype train | level 3, 60% to level 4, 4m 10s left, record level 5 |
| Goals | followers 1,234 of 1,500; subs 80 of 100 |
| Ads | next ad in 12m, 2 snoozes left, last ad 48m ago |
| Schedule | next segment Friday 2026-10-30 20:00 local, title `Bot night` |
| Chat activity | 18 messages per minute, 33 unique chatters this stream, top chatter today ViewerOne (140) |
| Platforms | Twitch 42, Kick 10, YouTube 8 viewers (60 in total) |
| Bot | name `SampleBot`, version `1.42.0`, up 3d 4h |
| Lookups (stubbed HTTP, never the live service) | Open-Meteo for `Amsterdam`: 12 °C, feels like 10 °C, WMO code 61 (`light rain`), timezone `Europe/Amsterdam`. Frankfurter `EUR` to `USD`: rate `1.162`, date 2026-10-26. Tests stub the `HttpMessageHandler`; they never call the live service. |

## 6. Code shape

**Name: `ITemplateHelperProvider`.** One provider owns one namespace. "Provider" is the word the codebase already uses
for a DI-registered source of something keyed by name (`IThirdPartyEmoteProvider`, `IMusicProvider`,
`IPronounProvider`). "Namespace" alone would clash with the C# keyword in speech and docs.

```csharp
public interface ITemplateHelperProvider
{
    string Namespace { get; }                                     // "music"
    IReadOnlyList<TemplateHelperEntry> Entries { get; }           // its rows, canonical keys and aliases
    ValueTask PrepareAsync(TemplateRenderScope scope,             // one fetch per Source for all nodes
        IReadOnlyCollection<HelperNode> nodes, CancellationToken ct);
    ValueTask<string?> RenderAsync(TemplateRenderScope scope,     // null = render the token as written
        HelperNode node, CancellationToken ct);
}
```

- `TemplateRenderScope` carries the broadcaster id, `ChannelContext`, the seed variables, the clock
  (`TimeProvider`), the channel zone and language, the `set:` variable bag and a per-render cache.
- Seed variables still win over a provider (as-built rule, `TemplateResolver.cs:56`).

**Folder layout** (beside the resolver, by domain, one file per responsibility):

```
server/src/NomNomzBot.Application/Abstractions/Templating/
    ITemplateHelperProvider.cs, ITemplateHelperRegistry.cs, TemplateHelperEntry.cs (grows the fields of section 4),
    TemplateDataSource.cs, HelperNode.cs, TemplateRenderScope.cs
server/src/NomNomzBot.Infrastructure/Platform/Templating/
    TemplateResolver.cs           (thin: parse, plan, dispatch by namespace, join)
    Parsing/TemplateParser.cs, Parsing/TemplateNode.cs
    TemplateHelperRegistry.cs     (the aggregate: every provider's Entries; ForContext)
    Helpers/Text/TextTemplateHelpers.cs          (+ TextTransformCatalog.cs moves here)
    Helpers/Numbers/NumberTemplateHelpers.cs     Helpers/Logic/LogicTemplateHelpers.cs
    Helpers/Stream/StreamTemplateHelpers.cs      Helpers/People/UserTemplateHelpers.cs (user, target, viewer)
    Helpers/Counters/CounterTemplateHelpers.cs   Helpers/Music/MusicTemplateHelpers.cs
    Helpers/LiveEvents/LiveEventTemplateHelpers.cs  Helpers/Ads/AdScheduleTemplateHelpers.cs
    Helpers/Activity/ChatActivityTemplateHelpers.cs Helpers/Emotes/EmoteTemplateHelpers.cs
    Helpers/Chat/ChatFormatTemplateHelpers.cs    Helpers/Platforms/PlatformTemplateHelpers.cs
    Helpers/Arguments/ArgumentTemplateHelpers.cs Helpers/Fun/FunTemplateHelpers.cs
    Helpers/Lookups/LookupTemplateHelpers.cs (+ one client per outside source)
    Helpers/Bot/BotTemplateHelpers.cs            Helpers/Events/EventSeedTemplateHelpers.cs (the seed keys)
    Dates/ (as-built, unchanged; wrapped by Helpers/Dates/DateTemplateHelpers.cs)
server/i18n/strings/values/strings_template_<namespace>.xml   (+ values-nl/ the same file names)
server/tests/NomNomzBot.Infrastructure.Tests/Platform/Templating/Helpers/<Namespace>/…Tests.cs
```

A provider with many keys splits by sub-domain (for example `MusicQueueHelpers` and `MusicNowPlayingHelpers`) when the
file passes one responsibility; no file holds more than one namespace.

**How the 171 + 14 as-built rows move.** L1 moves each as-built row, unchanged in key, context and help key, to the
provider of its namespace (`channel`, `stream` → Stream; `user`, `target`, `viewer`, pronoun grammar → People;
`count`, `list`, `custom` → Counters; `random`, `roll` → Logic; `transform` → Text; `date`, `time` → Dates; the
event-seed keys (`tier`, `bits`, `poll.*`, …) → Events; Discord and playlist seeds → Events). The resolver's
`ResolveBuiltInsAsync` groups move with them as each provider's `PrepareAsync`. The canonical key of each moved row is
in section 7.1; the old key becomes its alias.

**Registry.** The static `TemplateHelperRegistry.All` becomes the injected `ITemplateHelperRegistry` (an aggregate of
every provider's `Entries`). `TemplatesController`, `TemplateHelperValidator`, `TemplateSampleRenderer` and the
TypeScript writer read it. Providers are found by one reflection scan in L1 (the `StreamMetricTypeScript` pattern), so
no lane edits `DependencyInjection.cs`.

**i18n.** Each provider owns `strings_template_<namespace>.xml` in `values` and `values-nl`; key
`template_helper_<namespace>_<key>`. The as-built `template_helper_*` keys stay in `strings.xml` until L1 moves them
(one move, no rename, so no translation is lost). A test asserts every entry has an English and a Dutch string.

**Dashboard and SDK.** The dashboard reads `GET api/v1/templates/helpers?context=` (as-built route) and shows the list
grouped by namespace with search (a shadcn `Command` list; no UI is written in this lane). The SDK types come from the
same registry (section 11).

**Tests that replace `TemplateHelperCoverageTests`' source scan.** (1) Every entry renders its `Sample` against the
fixture of section 5. (2) Every alias resolves to the same output as its canonical key. (3) No two providers claim the
same key or alias. (4) Every entry has both strings. (5) The SDK type file lists exactly the registry keys.

## 7. Catalog

Columns: Name | Example | Output (fixed data, section 5) | Real data source | Lane. One row per helper form.
"pure" means the helper computes from its arguments only, in the named file.

### 7.1 As-built keys and their canonical names (aliases; nothing breaks)

| Name (as-built key) | Example (canonical) | Output | Real data source | Lane |
|---|---|---|---|---|
| `channel` | `{channel.login}` | samplechannel | `ChannelContext.ChannelName` (`IChannelRegistry.cs`) | L1 |
| `channel.display` | `{channel.display}` | SampleChannel | `ChannelContext.DisplayName` | L1 |
| `channel.id` | `{channel.id}` | 123456 | `ChannelContext.TwitchChannelId` | L1 |
| `streamer` | `{channel.display}` | SampleChannel | `ChannelContext.DisplayName` | L1 |
| `stream.title` | `{stream.title}` | Building a bot | `ChannelContext.CurrentTitle` | L1 |
| `stream.game` | `{stream.game}` | Software and Game Development | `ChannelContext.CurrentGame` | L1 |
| `stream.uptime` | `{stream.uptime}` | 2h 15m | `ChannelContext.WentLiveAt` | L1 |
| `stream.viewers` | `{stream.viewers}` | 42 | `ChannelContext.ViewerCount` | L1 |
| `stream.isLive` | `{stream.isLive}` | true | `ChannelContext.IsLive` | L1 |
| `stream.startedAt` | `{stream.startedAt}` | 2026-10-26T12:15:00.0000000+00:00 | `ChannelContext.WentLiveAt` | L1 |
| `tts.audioconnected` | `{tts.audioConnected}` | true | `TemplateResolver.ResolveAudioSourceConnected` | L1 |
| `status` | `{stream.status}` | live | `ChannelContext.IsLive` | L1 |
| `tense` | `{stream.tense}` | is | pronoun grammar + `IsLive` | L1 |
| `provider` | `{event.platform}` | twitch | event seed | L1 |
| `ad.duration` | `{event.ad.duration}` | 90 | event seed (channel.ad_break.begin) | L1 |
| `ad.when` | `{event.ad.when}` | in 5 minutes | event seed (ad upcoming) | L1 |
| `ad.seconds` | `{event.ad.seconds}` | 300 | event seed | L1 |
| `ad.automatic` | `{event.ad.automatic}` | true | event seed | L1 |
| `poll.title` | `{event.poll.title}` | Best snack? | event seed (channel.poll.*) | L1 |
| `poll.status` | `{event.poll.status}` | completed | event seed | L1 |
| `poll.winner` | `{event.poll.winner}` | chips | event seed | L1 |
| `poll.winner.votes` | `{event.poll.winner.votes}` | 12 | event seed | L1 |
| `poll.winner.percentage` | `{event.poll.winner.percentage}` | 60 | event seed | L1 |
| `poll.results` | `{event.poll.results}` | chips 12, cookies 8 | event seed | L1 |
| `poll.id` | `{event.poll.id}` | poll-1 | event seed | L1 |
| `poll.duration` | `{event.poll.duration}` | 120 | event seed | L1 |
| `poll.choices` | `{event.poll.choices}` | chips, cookies | event seed | L1 |
| `prediction.id` | `{event.prediction.id}` | pred-1 | event seed (channel.prediction.*) | L1 |
| `prediction.title` | `{event.prediction.title}` | Win? | event seed | L1 |
| `prediction.window` | `{event.prediction.window}` | 60 | event seed | L1 |
| `prediction.outcomes` | `{event.prediction.outcomes}` | Yes, No | event seed | L1 |
| `prediction.total_points` | `{event.prediction.totalPoints}` | 4000 | event seed | L1 |
| `prediction.status` | `{event.prediction.status}` | resolved | event seed | L1 |
| `prediction.winner` | `{event.prediction.winner}` | Yes | event seed | L1 |
| `prediction.winner.points` | `{event.prediction.winner.points}` | 3000 | event seed | L1 |
| `prediction.winner.users` | `{event.prediction.winner.users}` | 9 | event seed | L1 |
| `hype_train.id` | `{event.hypetrain.id}` | ht-1 | event seed (channel.hype_train.*) | L1 |
| `hype_train.level` | `{event.hypetrain.level}` | 3 | event seed | L1 |
| `hype_train.total` | `{event.hypetrain.total}` | 1800 | event seed | L1 |
| `hype_train.goal` | `{event.hypetrain.goal}` | 3000 | event seed | L1 |
| `followed_at` | `{event.followedAt}` | 2025-09-10T18:00:00Z | event seed (channel.follow) | L1 |
| `tier` | `{event.tier}` | 1 | event seed (subscribe) | L1 |
| `months` | `{event.months}` | 7 | event seed | L1 |
| `streak` | `{event.streak}` | 7 | event seed | L1 |
| `message` | `{event.message}` | love the stream | event seed | L1 |
| `also_said` | `{event.alsoSaid}` | and hello | event seed | L1 |
| `count` | `{event.count}` | 5 | event seed (gift count) | L1 |
| `anonymous` | `{event.anonymous}` | false | event seed | L1 |
| `bits` | `{event.bits}` | 100 | event seed (cheer) | L1 |
| `gifter` | `{event.gifter}` | ViewerTwo | event seed (gift received) | L1 |
| `gifter.id` | `{event.gifter.id}` | 1002 | event seed | L1 |
| `viewers` | `{event.viewers}` | 25 | event seed (raid) | L1 |
| `reward` | `{event.reward}` | Hydrate | event seed (redemption) | L1 |
| `reward.id` | `{event.reward.id}` | rw-1 | event seed | L1 |
| `redemption.id` | `{event.redemption.id}` | rd-1 | event seed | L1 |
| `cost` | `{event.cost}` | 500 | event seed | L1 |
| `input` | `{event.input}` | play song A | event seed | L1 |
| `moderator` | `{event.moderator}` | ModOne | event seed (ban) | L1 |
| `reason` | `{event.reason}` | spam | event seed | L1 |
| `duration` | `{event.duration}` | 600 | event seed | L1 |
| `broadcaster` | `{event.broadcaster}` | SampleChannel | event seed (stream.online) | L1 |
| `title` | `{event.title}` | Building a bot | event seed | L1 |
| `game` | `{event.game}` | Software and Game Development | event seed | L1 |
| `viewer.name` | `{event.viewer.name}` | ViewerOne | event seed (engagement.*) | L1 |
| `engagement.daysSinceLastSeen` | `{event.engagement.daysSinceLastSeen}` | 14 | event seed | L1 |
| `engagement.streak` | `{event.engagement.streak}` | 5 | event seed | L1 |
| `engagement.months` | `{event.engagement.months}` | 13 | event seed | L1 |
| `engagement.years` | `{event.engagement.years}` | 1 | event seed | L1 |
| `supporter.name` | `{event.supporter.name}` | ViewerOne | event seed (supporter.*) | L1 |
| `supporter.kind` | `{event.supporter.kind}` | tip | event seed | L1 |
| `supporter.amount` | `{event.supporter.amount}` | 5.00 | event seed | L1 |
| `supporter.currency` | `{event.supporter.currency}` | EUR | event seed | L1 |
| `supporter.tier` | `{event.supporter.tier}` | 1 | event seed | L1 |
| `supporter.quantity` | `{event.supporter.quantity}` | 1 | event seed | L1 |
| `supporter.message` | `{event.supporter.message}` | keep going | event seed | L1 |
| `obs.event.<field>` | `{event.obs.<field>}` | Scene 2 | event seed (obs-control.md §6) | L1 |
| `vts.event.<field>` | `{event.vts.<field>}` | Wave | event seed (vtube-studio.md §4) | L1 |
| `time` | `{time.now}` | 15:30:00 | `TimeProvider` + `ChannelContext.Timezone` | L1 |
| `time.utc` | `{time.utc}` | 14:30:00 UTC | `TimeProvider` | L1 |
| `date` | `{date}` | 2026-10-26 | `TimeProvider` + zone | L1 |
| `botname` | `{bot.name}` | SampleBot | `TemplateResolver.GetBotNameAsync` | L1 |
| `random.user` | `{random.chatter}` | ViewerOne or ViewerTwo | `ChannelContext.SessionChatters` | L1 |
| `random.number.<n>` | `{random.number:10}` | 1..10 | pure: `RandomNumberSpec.cs` | L1 |
| `random.number.<min>.<max>[.<step>]` | `{random.number:5\|50\|5}` | 5, 10 … 50 | pure: `RandomNumberSpec.cs` | L1 |
| `roll.<name>.<min>.<max>[.<step>]` | `{roll.love:0\|100}` | 0..100 | pure: `TemplateResolver.ResolveRolls` | L1 |
| `roll.<name>` | `{roll.love}` | the value rolled above | render scope | L1 |
| `roll.<name>.complement` | `{roll.love.complement}` | 100 minus the roll | render scope | L1 |
| `random.pick.<a.b.c>` | `{pick:a\|b\|c}` | a, b or c | `NoImmediateRepeatPicker` | L1 |
| `count.<key>` | `{count.deaths}` | 12 | `INamedCounterService` | L1 |
| `list.pick.<name>` | `{list.pick:snacks}` | chips, cookies or fruit | `IPickListService` | L1 |
| `custom.<name>.<field>` | `{custom.<name>.<field>}` | 21.5 | custom-data latest value (`TemplateResolver.ExpandCustomDataAsync`) | L1 |
| `transform.<name>:<text>` | `{text.<name>:<text>}` | HELLO | pure: `TextTransformCatalog.cs` | L1 |
| `args` | `{args.all}` | @ViewerTwo hello big world | command seed | L1 |
| `args.<n>` | `{args.1}` | @ViewerTwo | command seed | L1 |
| `user` | `{user.display}` | ViewerOne | trigger seed | L1 |
| `user.name` | `{user.login}` | viewerone | trigger seed | L1 |
| `user.id` | `{user.id}` | 1001 | trigger seed | L1 |
| `user.provider` | `{user.platform}` | twitch | trigger seed | L1 |
| `user.accountAge` | `{user.accountAge}` | 4 years, 2 months | `IUserService` (users table) | L1 |
| `user.followAge` | `{user.followAge}` | 1 year, 1 month | `TemplateResolver.ResolveFollowAgeAsync` | L1 |
| `user.pronouns` | `{user.pronouns}` | she/her | `IPronounResolutionService` | L1 |
| `user.messageCount` | `{user.messageCount}` | 1204 | chat messages table (`ResolveUserDbFieldsAsync`) | L1 |
| `user.lastmessage` | `{user.lastMessage}` | hello there | `ResolveLastMessagesAsync` | L1 |
| `user.link` | `{user.link}` | twitch.tv/viewerone | trigger seed | L1 |
| `viewer.messages` | `{user.stats.messages}` | 1,204 | `IViewerAnalyticsService.GetProfileAsync` | L1 |
| `viewer.watchtime` | `{user.stats.watchTime}` | 12h 30m | `ViewerProfileDto.TotalWatchSeconds` | L1 |
| `viewer.firstseen` | `{user.stats.firstSeen}` | 2025-09-10 | `ViewerProfileDto.FirstSeenAt` | L1 |
| `viewer.redemptions` | `{user.stats.redemptions}` | 31 | `ViewerProfileDto.TotalRedemptions` | L1 |
| `viewer.songrequests` | `{user.stats.songRequests}` | 9 | `ViewerProfileDto.TotalSongRequests` | L1 |
| `viewer.commands` | `{user.stats.commands}` | 77 | `ViewerProfileDto.TotalCommandsUsed` | L1 |
| `viewer.days` | `{user.stats.days}` | 412 | `ViewerProfileDto.FirstSeenAt` | L1 |
| `viewer.avgperday` | `{user.stats.avgPerDay}` | 2.9 | `ViewerProfileDto` | L1 |
| `viewer.botpercent` | `{user.stats.botPercent}` | 6% | `ViewerProfileDto` | L1 |
| `viewer.data.<key>` | `{user.data.<key>}` | blue | `IViewerDataService` | L1 |
| `target` | `{target.display}` | ViewerTwo | command seed | L1 |
| `target.name` | `{target.login}` | viewertwo | `ResolveTargetAsync` | L1 |
| `target.displayname` | `{target.display}` | ViewerTwo | `ResolveTargetAsync` | L1 |
| `target.known` | `{target.known}` | true | `ResolveTargetAsync` | L1 |
| `target.id` | `{target.id}` | 1002 | `ResolveTargetAsync` | L1 |
| `target.followAge` | `{target.followAge}` | 9 months | `ResolveFollowAgeAsync` | L1 |
| `target.lastmessage` | `{target.lastMessage}` | gg | `ResolveLastMessagesAsync` | L1 |
| `target.lastmessage.full` | `{target.lastMessage.full}` | gg well played | `ResolveLastMessagesAsync` | L1 |
| `target.randommessage` | `{target.randomMessage}` | gg | `ResolveLastMessagesAsync` | L1 |
| `target.link` | `{target.link}` | twitch.tv/viewertwo | seed | L1 |
| `target.messages` | `{target.stats.messages}` | 310 | `IViewerAnalyticsService` | L1 |
| `target.watchtime` | `{target.stats.watchTime}` | 4h 5m | `IViewerAnalyticsService` | L1 |
| `target.firstseen` | `{target.stats.firstSeen}` | 2026-01-05 | `IViewerAnalyticsService` | L1 |
| `target.redemptions` | `{target.stats.redemptions}` | 4 | `IViewerAnalyticsService` | L1 |
| `target.songrequests` | `{target.stats.songRequests}` | 2 | `IViewerAnalyticsService` | L1 |
| `target.commands` | `{target.stats.commands}` | 12 | `IViewerAnalyticsService` | L1 |
| `target.days` | `{target.stats.days}` | 294 | `IViewerAnalyticsService` | L1 |
| `target.avgperday` | `{target.stats.avgPerDay}` | 1.1 | `IViewerAnalyticsService` | L1 |
| `target.botpercent` | `{target.stats.botPercent}` | 2% | `IViewerAnalyticsService` | L1 |
| `target.data.<key>` | `{target.data.<key>}` | red | `IViewerDataService` | L1 |
| `name` | `{name}` | ViewerTwo | target else user | L1 |
| `link` | `{link}` | twitch.tv/viewertwo | target else user | L1 |
| `subject` | `{subject}` | he | `IPronounResolutionService` | L1 |
| `object` | `{object}` | him | `IPronounResolutionService` | L1 |
| `possessive` | `{possessive}` | his | `IPronounResolutionService` | L1 |
| `presentTense` | `{presentTense}` | is | `IPronounResolutionService` | L1 |
| `genderedTerm` | `{genderedTerm}` | guy | `IPronounResolutionService` | L1 |
| `user.subject` | `{user.subject}` | she | `IPronounResolutionService` | L1 |
| `user.object` | `{user.object}` | her | `IPronounResolutionService` | L1 |
| `user.possessive` | `{user.possessive}` | her | `IPronounResolutionService` | L1 |
| `user.presentTense` | `{user.presentTense}` | is | `IPronounResolutionService` | L1 |
| `user.genderedTerm` | `{user.genderedTerm}` | girl | `IPronounResolutionService` | L1 |
| `user.tense` | `{user.tense}` | is | grammar + `IsLive` | L1 |
| `target.subject` | `{target.subject}` | he | `IPronounResolutionService` | L1 |
| `target.object` | `{target.object}` | him | `IPronounResolutionService` | L1 |
| `target.possessive` | `{target.possessive}` | his | `IPronounResolutionService` | L1 |
| `target.presentTense` | `{target.presentTense}` | is | `IPronounResolutionService` | L1 |
| `target.genderedTerm` | `{target.genderedTerm}` | guy | `IPronounResolutionService` | L1 |
| `target.tense` | `{target.tense}` | is | grammar + `IsLive` | L1 |
| `verb:<sing\|plur>` | `{verb:plays\|play}` | plays | `TemplateResolver.ResolveVerbAgreement` | L1 |
| `user.verb:<sing\|plur>` | `{user.verb:plays\|play}` | plays | `ResolveVerbAgreement` | L1 |
| `target.verb:<sing\|plur>` | `{target.verb:plays\|play}` | plays | `ResolveVerbAgreement` | L1 |
| `channel.name` | `{channel.display}` (Discord) | SampleChannel | Discord seed (`DiscordGoLiveNotificationHandler`) | L1 |
| `channel.title` | `{stream.title}` (Discord) | Building a bot | Discord seed | L1 |
| `channel.game` | `{stream.game}` (Discord) | Software and Game Development | Discord seed | L1 |
| `channel.url` | `{channel.url}` | https://twitch.tv/samplechannel | Discord seed | L1 |
| `stream.url` | `{stream.url}` | https://twitch.tv/samplechannel | Discord seed | L1 |
| `channel.login` | `{channel.login}` | samplechannel | Discord seed | L1 |
| `channel.avatar` | `{channel.avatar}` | https://static-cdn.jtvnw.net/…/avatar.png | Discord seed | L1 |
| `stream.thumbnail` | `{stream.thumbnail}` | https://static-cdn.jtvnw.net/…/thumb.jpg | Discord seed | L1 |
| `raw.message` | `{event.rawMessage}` | hello | Discord seed | L1 |
| `playlist_id` | `{event.playlist.id}` | pl-1 | `PlaylistAddAction` seed | L1 |
| `track_name` | `{event.track.name}` | Song A | `PlaylistAddAction` seed | L1 |
| `music.favorite.track` | `{music.favorite.track}` | Song A | `song_request_favorite` seed | L1 |
| `music.favorite.artist` | `{music.favorite.artist}` | Artist A | `song_request_favorite` seed | L1 |
| `music.favorite.count` | `{music.favorite.count}` | 3 | `song_request_favorite` seed | L1 |
| `music.favorite.outcome` | `{music.favorite.outcome}` | queued | `song_request_favorite` seed | L1 |

### 7.2 Date expressions (as-built on `master`, commit `bf2487658`; kept as they are)

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `date:<source>` | `{date:now}` | 2026-10-26 15:30 | `Templating/Dates/DateSourceReader.cs` | L1 |
| `date:<source>\|format:<pattern>` | `{date:now\|format:dddd 'the' [o] 'of' MMMM}` | Monday the 26th of October | `Dates/DateOutputFormatter.cs` | L1 |
| `date:<source>\|<+/-amount><unit>` | `{date:now\|+1d\|format:yyyy-MM-dd}` | 2026-10-27 | `Dates/DateCalculator.cs` | L1 |
| `date:<source>\|start:<unit>` | `{date:now\|start:month\|format:yyyy-MM-dd}` | 2026-10-01 | `Dates/DateCalculator.cs` | L1 |
| `date:<source>\|end:<unit>` | `{date:now\|end:day\|format:HH:mm:ss}` | 23:59:59 | `Dates/DateCalculator.cs` | L1 |
| `date:<source>\|next:<weekday>` | `{date:now\|next:friday\|format:ddd d MMM}` | Fri 30 Oct | `Dates/DateCalculator.cs` | L1 |
| `date:<source>\|prev:<weekday>` | `{date:now\|prev:sunday\|format:ddd d MMM}` | Sun 25 Oct | `Dates/DateCalculator.cs` | L1 |
| `date:<source>\|tz:<zone>` | `{date:now\|tz:America/New_York\|format:HH:mm}` | 10:30 | `Dates/DateCalculator.cs` (NodaTime) | L1 |
| `date:<source>\|lang:<language>` | `{date:now\|lang:nl\|format:dddd}` | maandag (Dutch output) | `Dates/DateExpressionContext.cs` | L1 |
| `date:<source>\|relative` | `{date:now\|-3d\|relative}` | 3 days ago | `Dates/DateOutputFormatter.cs` (Humanizer) | L1 |
| `date:<source>\|ordinal` | `{date:now\|ordinal}` | 26th | `Dates/DateOutputFormatter.cs` | L1 |
| `date:<source>\|until[:<unit>]` | `{date:2026-12-25\|until:days}` | 59 days | `Dates/DateSpanFormatter.cs` | L1 |
| `date:<source>\|since[:<unit>]` | `{date:{followed_at}\|since:breakdown}` | 1 year, 1 month, 15 days, 19 hours, 30 minutes | `Dates/DateSpanFormatter.cs` | L1 |
| `date:<source>\|diff:<date>[:<unit>]` | `{date:2026-12-25\|diff:2026-12-31:days}` | 6 days | `Dates/DateSpanFormatter.cs` | L1 |

### 7.3 `text` — text tools (group 1, lane L2a)

All rows are pure functions in `Helpers/Text/` beside the as-built `TextTransformCatalog.cs`; they work on text
elements (graphemes), as the as-built catalog does. Source column: "pure" = `TextTransformCatalog.cs` (extended).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `text.upper:<t>` | `{text.upper:hello}` | HELLO | pure | L2a |
| `text.lower:<t>` | `{text.lower:HeLLo}` | hello | pure | L2a |
| `text.title:<t>` | `{text.title:big world}` | Big World | pure | L2a |
| `text.sentence:<t>` | `{text.sentence:hello WORLD. bye}` | Hello world. Bye | pure | L2a |
| `text.capitalize:<t>` | `{text.capitalize:hello world}` | Hello world | pure | L2a |
| `text.decapitalize:<t>` | `{text.decapitalize:Hello}` | hello | pure | L2a |
| `text.swapcase:<t>` | `{text.swapcase:Hello}` | hELLO | pure | L2a |
| `text.spaced:<t>` | `{text.spaced:hype}` | h y p e | pure | L2a |
| `text.spaced:<t>\|<sep>` | `{text.spaced:hype\|.}` | h.y.p.e | pure | L2a |
| `text.alternating:<t>` | `{text.alternating:hello}` | HeLlO | pure | L2a |
| `text.mocking:<t>` | `{text.mocking:hello}` | hElLo | pure | L2a |
| `text.spongecase:<t>` | `{text.spongecase:hello world}` | hElLo WoRlD | pure | L2a |
| `text.reverse:<t>` | `{text.reverse:abc}` | cba | pure | L2a |
| `text.reverse.words:<t>` | `{text.reverse.words:hello big world}` | world big hello | pure | L2a |
| `text.trim:<t>` | `{text.trim:  hi  }` | hi | pure | L2a |
| `text.squish:<t>` | `{text.squish:a    b  c}` | a b c | pure | L2a |
| `text.truncate:<n>\|<t>` | `{text.truncate:5\|hello world}` | hello… | pure | L2a |
| `text.cut:<t>\|max=<n>\|end=<s>` | `{text.cut:hello world\|max=8\|end=...}` | hello... | pure | L2a |
| `text.cut.words:<t>\|<n>` | `{text.cut.words:hello big world\|2}` | hello big… | pure | L2a |
| `text.pad.left:<t>\|<n>[\|<c>]` | `{text.pad.left:7\|3\|0}` | 007 | pure | L2a |
| `text.pad.right:<t>\|<n>[\|<c>]` | `{text.pad.right:ab\|4\|.}` | ab.. | pure | L2a |
| `text.center:<t>\|<n>[\|<c>]` | `{text.center:hi\|6\|*}` | \*\*hi\*\* | pure | L2a |
| `text.repeat:<n>\|<t>` | `{text.repeat:3\|ha}` | hahaha | pure | L2a |
| `text.repeat:<n>\|<t>\|<sep>` | `{text.repeat:3\|ha\| }` | ha ha ha | pure | L2a |
| `text.replace:<t>\|<find>\|<with>` | `{text.replace:hello world\|world\|chat}` | hello chat | pure | L2a |
| `text.replace.first:<t>\|<find>\|<with>` | `{text.replace.first:a a a\|a\|b}` | b a a | pure | L2a |
| `text.replace.icase:<t>\|<find>\|<with>` | `{text.replace.icase:Hello\|hello\|bye}` | bye | pure | L2a |
| `text.remove:<t>\|<find>` | `{text.remove:hello world\|o}` | hell wrld | pure | L2a |
| `text.wrap:<t>\|<l>[\|<r>]` | `{text.wrap:hi\|[\|]}` | [hi] | pure | L2a |
| `text.quote:<t>` | `{text.quote:hi}` | "hi" | pure | L2a |
| `text.paren:<t>` | `{text.paren:hi}` | (hi) | pure | L2a |
| `text.lines.wrap:<t>\|<n>` | `{text.lines.wrap:aaa bbb ccc\|7}` | aaa bbb / ccc (2 lines) | pure | L2a |
| `text.words.count:<t>` | `{text.words.count:hello big world}` | 3 | pure | L2a |
| `text.chars.count:<t>` | `{text.chars.count:hello}` | 5 | pure | L2a |
| `text.letters.count:<t>` | `{text.letters.count:a1 b2}` | 2 | pure | L2a |
| `text.lines.count:<t>` | `{text.lines.count:a\nb}` | 2 | pure | L2a |
| `text.count:<t>\|<find>` | `{text.count:banana\|a}` | 3 | pure | L2a |
| `text.word.first:<t>` | `{text.word.first:hello big world}` | hello | pure | L2a |
| `text.word.last:<t>` | `{text.word.last:hello big world}` | world | pure | L2a |
| `text.word:<n>\|<t>` | `{text.word:2\|hello big world}` | big | pure | L2a |
| `text.words.first:<n>\|<t>` | `{text.words.first:2\|hello big world}` | hello big | pure | L2a |
| `text.words.last:<n>\|<t>` | `{text.words.last:2\|hello big world}` | big world | pure | L2a |
| `text.words.from:<n>\|<t>` | `{text.words.from:2\|hello big world}` | big world | pure | L2a |
| `text.words.range:<a>\|<b>\|<t>` | `{text.words.range:1\|2\|hello big world}` | hello big | pure | L2a |
| `text.words.shuffle:<t>` | `{text.words.shuffle:a b c}` | a permutation of a b c | pure (seeded) | L2a |
| `text.words.sort:<t>` | `{text.words.sort:c a b}` | a b c | pure | L2a |
| `text.words.unique:<t>` | `{text.words.unique:a b a}` | a b | pure | L2a |
| `text.word.random:<t>` | `{text.word.random:a b c}` | a, b or c | pure (seeded) | L2a |
| `text.word.longest:<t>` | `{text.word.longest:hi hello yo}` | hello | pure | L2a |
| `text.chars.shuffle:<t>` | `{text.chars.shuffle:abc}` | a permutation of abc | pure (seeded) | L2a |
| `text.first:<n>\|<t>` | `{text.first:3\|hello}` | hel | pure | L2a |
| `text.last:<n>\|<t>` | `{text.last:3\|hello}` | llo | pure | L2a |
| `text.substring:<from>\|<len>\|<t>` | `{text.substring:2\|3\|hello}` | ell | pure | L2a |
| `text.initials:<t>` | `{text.initials:big world}` | BW | pure | L2a |
| `text.slug:<t>` | `{text.slug:Hello Big World!}` | hello-big-world | pure | L2a |
| `text.snake:<t>` | `{text.snake:Hello Big World}` | hello_big_world | pure | L2a |
| `text.kebab:<t>` | `{text.kebab:Hello Big World}` | hello-big-world | pure | L2a |
| `text.camel:<t>` | `{text.camel:hello big world}` | helloBigWorld | pure | L2a |
| `text.pascal:<t>` | `{text.pascal:hello big world}` | HelloBigWorld | pure | L2a |
| `text.hashtag:<t>` | `{text.hashtag:big world}` | #BigWorld | pure | L2a |
| `text.url.encode:<t>` | `{text.url.encode:a b&c}` | a%20b%26c | pure (`Uri.EscapeDataString`) | L2a |
| `text.url.decode:<t>` | `{text.url.decode:a%20b}` | a b | pure | L2a |
| `text.query.encode:<t>` | `{text.query.encode:a b}` | a+b | pure | L2a |
| `text.path.encode:<t>` | `{text.path.encode:a/b c}` | a%2Fb%20c | pure | L2a |
| `text.base64.encode:<t>` | `{text.base64.encode:hi}` | aGk= | pure | L2a |
| `text.base64.decode:<t>` | `{text.base64.decode:aGk=}` | hi | pure | L2a |
| `text.html.strip:<t>` | `{text.html.strip:<b>hi</b>}` | hi | pure | L2a |
| `text.emotes.strip:<t>` | `{text.emotes.strip:hi Kappa there}` | hi there | `ChatEmoteCatalogue.cs` (channel emote names) | L2a |
| `text.links.strip:<t>` | `{text.links.strip:see https://a.b now}` | see now | pure | L2a |
| `text.mentions.strip:<t>` | `{text.mentions.strip:@ViewerTwo hi}` | hi | pure | L2a |
| `text.digits:<t>` | `{text.digits:a1b22}` | 122 | pure | L2a |
| `text.letters:<t>` | `{text.letters:a1b2}` | ab | pure | L2a |
| `text.vowels.remove:<t>` | `{text.vowels.remove:hello}` | hll | pure | L2a |
| `text.censor:<t>\|<word>` | `{text.censor:you noob\|noob}` | you \*\*\*\* | pure | L2a |
| `text.mask:<t>\|<keep>` | `{text.mask:secret\|2}` | \*\*\*\*et | pure | L2a |
| `text.smallcaps:<t>` | `{text.smallcaps:hello}` | ʜᴇʟʟᴏ | pure (Unicode map) | L2a |
| `text.upsidedown:<t>` | `{text.upsidedown:hello}` | ollǝɥ | pure (Unicode map) | L2a |
| `text.fullwidth:<t>` | `{text.fullwidth:hi}` | ｈｉ | pure (Unicode map) | L2a |
| `text.bold:<t>` | `{text.bold:hi}` | 𝐡𝐢 | pure (Unicode math bold) | L2a |
| `text.italic:<t>` | `{text.italic:hi}` | 𝘩𝘪 | pure (Unicode math italic) | L2a |
| `text.monospace:<t>` | `{text.monospace:hi}` | 𝚑𝚒 | pure (Unicode math mono) | L2a |
| `text.script:<t>` | `{text.script:hi}` | 𝒽𝒾 | pure (Unicode math script) | L2a |
| `text.bubble:<t>` | `{text.bubble:hi}` | ⓗⓘ | pure (Unicode circled) | L2a |
| `text.squared:<t>` | `{text.squared:HI}` | 🄷🄸 | pure (Unicode squared) | L2a |
| `text.strike:<t>` | `{text.strike:hi}` | h̶i̶ | pure (U+0336) | L2a |
| `text.underline:<t>` | `{text.underline:hi}` | h̲i̲ | pure (U+0332) | L2a |
| `text.superscript:<t>` | `{text.superscript:hi}` | ʰⁱ | pure (Unicode map) | L2a |
| `text.plain:<t>` | `{text.plain:𝐡𝐢 ⓗⓘ}` | hi hi | pure (NFKC fold; `MessageNormalizer.cs` style) | L2a |
| `text.leet:<t>` | `{text.leet:leet}` | l337 | pure | L2a |
| `text.rot13:<t>` | `{text.rot13:hello}` | uryyb | pure | L2a |
| `text.uwu:<t>` | `{text.uwu:really}` | weawwy | pure | L2a |
| `text.clap:<t>` | `{text.clap:big world}` | big 👏 world | pure | L2a |
| `text.clap:<t>\|<sep>` | `{text.clap:big world\|🔥}` | big 🔥 world | pure | L2a |
| `text.stutter:<t>` | `{text.stutter:hello}` | h-hello | pure | L2a |
| `text.yell:<t>` | `{text.yell:hello}` | HELLO! | pure | L2a |
| `text.plural:<n>\|<one>\|<many>` | `{text.plural:3\|sub\|subs}` | subs | pure | L2a |
| `plural:<n>\|<one>\|<many>` | `{plural:1\|sub\|subs}` | sub | pure (root alias of `text.plural`) | L2a |
| `text.plural.count:<n>\|<one>\|<many>` | `{text.plural.count:3\|sub\|subs}` | 3 subs | pure | L2a |
| `text.plural.auto:<n>\|<word>` | `{text.plural.auto:2\|box}` | boxes | pure (Humanizer `Pluralize`, dependency added by `bf2487658`) | L2a |
| `text.singular:<word>` | `{text.singular:boxes}` | box | pure (Humanizer `Singularize`) | L2a |
| `text.article:<word>` | `{text.article:apple}` | an apple | pure (English only; other languages return the word) | L2a |
| `text.possessive:<name>` | `{text.possessive:James}` | James' | pure | L2a |
| `text.join.and:<a>\|<b>\|<c>` | `{text.join.and:a\|b\|c}` | a, b and c | pure (channel language word for "and") | L2a |
| `text.join.or:<a>\|<b>\|<c>` | `{text.join.or:a\|b\|c}` | a, b or c | pure | L2a |
| `text.length.fit:<t>\|<n>` | `{text.length.fit:hello\|3}` | hel | pure (no ending) | L2a |
| `text.lines.first:<t>` | `{text.lines.first:a\nb}` | a | pure | L2a |
| `text.lines.join:<t>\|<sep>` | `{text.lines.join:a\nb\| / }` | a / b | pure | L2a |
| `text.escape.chat:<t>` | `{text.escape.chat:/ban x}` | ⁄ban x (a leading slash or dot is neutralised so chat never runs a command) | pure | L2a |

### 7.4 `num` and `math` — numbers and math (group 2, lane L2b)

Pure functions in `Helpers/Numbers/`. Number words, ordinals and Roman numerals use Humanizer (added by commit
`bf2487658` through `Directory.Packages.props`). Number and currency formats use `CultureInfo` of the channel
language (`ChannelContext.Language`); that commit turned `InvariantGlobalization` off, so `nl` formats render.
`math` uses a small safe expression evaluator (numbers, `+ - * / % ^ ( )`, the functions listed): no variables
other than resolved placeholders, no code. **Not checked:** which evaluator library; L2b picks one in its slice and
records it (or writes a 100-line shunting-yard parser, which is allowed: no library is a dependency rule here).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `math:<expr>` | `{math:{event.bits}/100}` (bits 250) | 2.5 | pure | L2b |
| `math:<expr>` precedence | `{math:2+3*4}` | 14 | pure | L2b |
| `math:<expr>` brackets | `{math:(2+3)*4}` | 20 | pure | L2b |
| `math:<expr>` power | `{math:2^10}` | 1024 | pure | L2b |
| `math:<expr>` modulo | `{math:17%5}` | 2 | pure | L2b |
| `math:<expr>` with counter | `{math:{count.deaths}*2}` | 24 | pure + `INamedCounterService` | L2b |
| `math.sqrt:<n>` | `{math.sqrt:81}` | 9 | pure | L2b |
| `math.abs:<n>` | `{math.abs:-4}` | 4 | pure | L2b |
| `math.floor:<n>` | `{math.floor:2.7}` | 2 | pure | L2b |
| `math.ceil:<n>` | `{math.ceil:2.1}` | 3 | pure | L2b |
| `math.pow:<a>\|<b>` | `{math.pow:3\|3}` | 27 | pure | L2b |
| `math.log:<n>[\|<base>]` | `{math.log:100\|10}` | 2 | pure | L2b |
| `math.add:<a>\|<b>` | `{math.add:2\|3}` | 5 | pure | L2b |
| `math.sub:<a>\|<b>` | `{math.sub:5\|3}` | 2 | pure | L2b |
| `math.mul:<a>\|<b>` | `{math.mul:4\|3}` | 12 | pure | L2b |
| `math.div:<a>\|<b>` | `{math.div:7\|2}` | 3.5 | pure | L2b |
| `math.div.int:<a>\|<b>` | `{math.div.int:7\|2}` | 3 | pure | L2b |
| `math.mod:<a>\|<b>` | `{math.mod:7\|2}` | 1 | pure | L2b |
| `math.sum:<a>\|<b>\|…` | `{math.sum:1\|2\|3}` | 6 | pure | L2b |
| `math.avg:<a>\|<b>\|…` | `{math.avg:1\|2\|6}` | 3 | pure | L2b |
| `math.median:<a>\|…` | `{math.median:1\|9\|3}` | 3 | pure | L2b |
| `num.min:<a>\|<b>\|…` | `{num.min:4\|9\|2}` | 2 | pure | L2b |
| `num.max:<a>\|<b>\|…` | `{num.max:4\|9\|2}` | 9 | pure | L2b |
| `num.clamp:<n>\|<min>\|<max>` | `{num.clamp:120\|0\|100}` | 100 | pure | L2b |
| `num.round:<n>[\|<places>]` | `{num.round:2.456\|2}` | 2.46 | pure | L2b |
| `num.round:<n>` | `{num.round:2.5}` | 3 (away from zero) | pure | L2b |
| `num.truncate:<n>` | `{num.truncate:2.9}` | 2 | pure | L2b |
| `num.percent:<part>\|<whole>` | `{num.percent:1234\|1500}` | 82% | pure | L2b |
| `num.percent:<part>\|<whole>\|<places>` | `{num.percent:1234\|1500\|1}` | 82.3% | pure | L2b |
| `num.percentOf:<p>\|<n>` | `{num.percentOf:10\|250}` | 25 | pure | L2b |
| `num.ratio:<a>\|<b>` | `{num.ratio:3\|12}` | 1:4 | pure | L2b |
| `num.diff:<a>\|<b>` | `{num.diff:2800\|2500}` | 300 | pure | L2b |
| `num.format:<n>` | `{num.format:1234}` | 1,234 | pure (channel culture) | L2b |
| `num.format:<n>` (Dutch channel) | `{num.format:1234}` | 1.234 (Dutch output) | pure | L2b |
| `num.format:<n>\|<places>` | `{num.format:1234.5\|2}` | 1,234.50 | pure | L2b |
| `num.compact:<n>` | `{num.compact:1234}` | 1.2k | pure | L2b |
| `num.compact:<n>` millions | `{num.compact:3400000}` | 3.4M | pure | L2b |
| `num.compact:<n>` billions | `{num.compact:2100000000}` | 2.1B | pure | L2b |
| `num.ordinal:<n>` | `{num.ordinal:3}` | 3rd | Humanizer `Ordinalize` | L2b |
| `num.ordinal:<n>` (Dutch channel) | `{num.ordinal:3}` | 3e (Dutch output) | Humanizer | L2b |
| `num.ordinal.words:<n>` | `{num.ordinal.words:3}` | third | Humanizer `ToOrdinalWords` | L2b |
| `num.words:<n>` | `{num.words:42}` | forty-two | Humanizer `ToWords` | L2b |
| `num.words:<n>` (Dutch channel) | `{num.words:42}` | tweeënveertig (Dutch output) | Humanizer | L2b |
| `num.roman:<n>` | `{num.roman:2026}` | MMXXVI | Humanizer `ToRoman` | L2b |
| `num.fromRoman:<r>` | `{num.fromRoman:XIV}` | 14 | Humanizer `FromRoman` | L2b |
| `num.currency:<n>` | `{num.currency:5}` | €5.00 (channel currency) | pure; currency code from `ICurrencyConfigService`? see D2 | L2b |
| `num.currency:<n>\|<code>` | `{num.currency:5\|USD}` | $5.00 | pure (ISO 4217 + culture) | L2b |
| `num.currency.compact:<n>\|<code>` | `{num.currency.compact:1500\|USD}` | $1.5k | pure | L2b |
| `num.bytes:<n>` | `{num.bytes:1536}` | 1.5 KB | Humanizer `Bytes` | L2b |
| `num.duration:<seconds>` | `{num.duration:8100}` | 2h 15m | pure (`TemplateResolver.FormatUptime` style) | L2b |
| `num.duration.long:<seconds>` | `{num.duration.long:8100}` | 2 hours, 15 minutes | Humanizer `TimeSpan.Humanize(2)` | L2b |
| `num.duration.clock:<seconds>` | `{num.duration.clock:83}` | 1:23 | pure | L2b |
| `num.sign:<n>` | `{num.sign:5}` | +5 | pure | L2b |
| `num.abs:<n>` | `{num.abs:-5}` | 5 | pure | L2b |
| `num.parity:<n>` | `{num.parity:7}` | odd | pure (word, channel language) | L2b |
| `num.isEven:<n>` | `{if:{num.isEven:8}\|even\|odd}` | even | pure (boolean for `if`) | L2b |
| `num.between:<n>\|<a>\|<b>` | `{if:{num.between:5\|1\|10}\|in range\|out}` | in range | pure (boolean) | L2b |
| `num.parse:<t>` | `{num.parse:1.2k}` | 1200 | pure | L2b |
| `num.digits:<n>` | `{num.digits:12345}` | 5 | pure | L2b |
| `num.digitSum:<n>` | `{num.digitSum:123}` | 6 | pure | L2b |
| `num.inc:<n>` | `{num.inc:4}` | 5 | pure | L2b |
| `num.dec:<n>` | `{num.dec:4}` | 3 | pure | L2b |
| `num.hex:<n>` | `{num.hex:255}` | FF | pure | L2b |
| `num.binary:<n>` | `{num.binary:5}` | 101 | pure | L2b |
| `num.convert:<n>\|<from>\|<to>` length | `{num.convert:10\|km\|mi}` | 6.21 | pure (fixed unit table) | L2b |
| `num.convert:<n>\|c\|f` temperature | `{num.convert:20\|c\|f}` | 68 | pure | L2b |
| `num.convert:<n>\|kg\|lb` | `{num.convert:2\|kg\|lb}` | 4.41 | pure | L2b |
| `num.convert:<n>\|cm\|in` | `{num.convert:180\|cm\|in}` | 70.87 | pure | L2b |
| `num.convert:<n>\|cm\|ft` | `{num.convert:180\|cm\|ft}` | 5'11" | pure | L2b |
| `num.bar:<value>\|<max>` | `{num.bar:6\|10}` | ██████░░░░ | pure (alias of `meter`) | L2b |
| `meter:<percent>` | `{meter:60}` | ██████░░░░ 60% | pure (group 17) | L2b |
| `meter:<percent>\|<width>` | `{meter:60\|5}` | ███░░ 60% | pure | L2b |
| `meter:<percent>\|<width>\|<full>\|<empty>` | `{meter:60\|5\|🟩\|⬜}` | 🟩🟩🟩⬜⬜ 60% | pure | L2b |
| `meter.plain:<percent>` | `{meter.plain:60}` | ██████░░░░ | pure (no number) | L2b |
| `meter.hearts:<percent>` | `{meter.hearts:60}` | ❤️❤️❤️🖤🖤 | pure | L2b |
| `meter.stars:<n>\|<max>` | `{meter.stars:3\|5}` | ★★★☆☆ | pure | L2b |

**OPEN DECISION D2 (Stoney): the channel currency.** `num.currency:<n>` with no code needs a channel currency. The
channel settings hold a language and a zone (`ChannelContext.Language`, `.Timezone`), not a money currency
(`ICurrencyConfigService` is the loyalty-points economy, not money; read 2026-10-26). Options: (a) no default, the
code is a required argument; (b) a new channel setting `MoneyCurrency` (neutral default `USD`, editable). Until he
picks, L2b builds (a) and the no-code row renders the number with 2 decimals and no symbol.

### 7.5 Logic, composing and random (groups 3 and 16, lane L2c)

Pure in `Helpers/Logic/` unless the row names a service. `if` follows spec §6.3.2 (boolean path and value
predicate); this catalog adds the root form `{if:<value>|<then>|<else>}`, where `<value>` is true when it is a
non-empty text that is not `false`, `0` or `no`.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `if:<value>\|<then>\|<else>` | `{if:{user.isSub}\|thanks sub!\|sub for perks}` | thanks sub! | pure + `user.isSub` | L2c |
| `if:<value>\|<then>` | `{if:{user.isVip}\|💎}` | (empty: not VIP) | pure | L2c |
| `if.<path>:<then>\|<else>` | `{if.stream.live:on\|off}` | on | spec §6.3.2 form A | L2c |
| `if.<path>.equals:<v>:<then>\|<else>` | `{if.user.login.equals:viewerone:hi me\|hi}` | hi me | spec §6.3.2 form B | L2c |
| `if.<path>.iequals:<v>:…` | `{if.user.login.iequals:VIEWERONE:yes\|no}` | yes | spec §6.3.2 | L2c |
| `if.<path>.contains:<v>:…` | `{if.args.all.contains:hello:hi back\|}` | hi back | spec §6.3.2 | L2c |
| `if.<path>.startswith:<v>:…` | `{if.args.1.startswith:@:mention\|word}` | mention | spec §6.3.2 | L2c |
| `if.<path>.endswith:<v>:…` | `{if.args.all.endswith:world:yes\|no}` | yes | spec §6.3.2 | L2c |
| `if.<path>.matches:<re>:…` | `{if.args.2.matches:^h:h-word\|other}` | h-word | spec §6.3.2 (`IRegexMatcher`, spec §6.4, not checked) | L2c |
| `if.<path>.gt:<n>:…` | `{if.count.deaths.gt:10:rough day\|ok}` | rough day | spec §6.3.2 | L2c |
| `if.<path>.lt:<n>:…` | `{if.stream.viewers.lt:10:cozy\|busy}` | busy | spec §6.3.2 | L2c |
| `if.<path>.gte:<n>:…` | `{if.user.months.gte:6:veteran\|new}` | veteran | spec §6.3.2 | L2c |
| `if.<path>.lte:<n>:…` | `{if.count.wins.lte:3:keep going\|great}` | keep going | spec §6.3.2 | L2c |
| `if.not:<value>\|<then>\|<else>` | `{if.not:{user.isMod}\|not a mod\|mod}` | not a mod | pure | L2c |
| `if.all:<a>\|<b>\|<then>\|<else>` | `{if.all:{user.isSub}\|{stream.isLive}\|both\|no}` | both | pure | L2c |
| `if.any:<a>\|<b>\|<then>\|<else>` | `{if.any:{user.isMod}\|{user.isSub}\|yes\|no}` | yes | pure | L2c |
| `if.empty:<value>\|<then>\|<else>` | `{if.empty:{args.1}\|who?\|hi {args.1}}` | hi @ViewerTwo | pure | L2c |
| `default:<value>\|<fallback>` | `{default:{stream.game}\|nothing}` | Software and Game Development | pure | L2c |
| `default:<value>\|<fallback>` (empty) | `{default:{args.5}\|nobody}` | nobody | pure | L2c |
| `coalesce:<a>\|<b>\|<c>` | `{coalesce:{args.5}\|{target.display}\|{user.display}}` | ViewerTwo | pure | L2c |
| `switch:<value>\|<case>=<out>\|…\|<else>` | `{switch:{user.pronouns}\|she/her=queen\|he/him=king\|royal}` | queen | pure | L2c |
| `switch.range:<n>\|<upTo>=<out>\|…` | `{switch.range:{count.deaths}\|5=fine\|10=hmm\|oof}` | oof | pure | L2c |
| `chance:<percent>\|<then>[\|<else>]` | `{chance:10\|jackpot!\|no luck}` | jackpot! (10%) or no luck | pure (seeded) | L2c |
| `pick:<a>\|<b>\|<c>` | `{pick:rock\|paper\|scissors}` | rock, paper or scissors | `NoImmediateRepeatPicker` (`Application/Common/Picking`) | L2c |
| `pick.weighted:<w>=<a>\|<w>=<b>` | `{pick.weighted:1=rare\|9=common}` | common (90%) or rare | pure (seeded) | L2c |
| `pick.n:<n>\|<a>\|<b>\|<c>` | `{pick.n:2\|a\|b\|c}` | 2 of a, b, c | pure (seeded) | L2c |
| `pick.shuffle:<a>\|<b>\|<c>` | `{pick.shuffle:a\|b\|c}` | a permutation, joined with ", " | pure (seeded) | L2c |
| `random.number:<max>` | `{random.number:6}` | 1..6 | `RandomNumberSpec.cs` | L2c |
| `random.number:<min>\|<max>[\|<step>]` | `{random.number:10\|50\|10}` | 10, 20 … 50 | `RandomNumberSpec.cs` | L2c |
| `random.decimal:<min>\|<max>\|<places>` | `{random.decimal:0\|1\|2}` | 0.00..1.00 | pure (seeded) | L2c |
| `random.percent` | `{random.percent}` | 0%..100% | pure (seeded) | L2c |
| `random.bool` | `{if:{random.bool}\|heads\|tails}` | heads or tails | pure (seeded) | L2c |
| `random.letter` | `{random.letter}` | A..Z | pure (seeded) | L2c |
| `random.color` | `{random.color}` | #3FA7D6 (a hex color) | pure (seeded) | L2c |
| `random.chatter` | `{random.chatter}` | ViewerOne or ViewerTwo | `ChannelContext.SessionChatters` | L2c |
| `random.chatter.weighted` | `{random.chatter.weighted}` | ViewerOne (more likely: more messages) | `ChannelContext.SessionChatters` + chat messages this stream (`IChatSearchService`) | L2c |
| `random.chatter.notMe` | `{random.chatter.notMe}` | ViewerTwo | `ChannelContext.SessionChatters` minus user | L2c |
| `random.chatter.sub` | `{random.chatter.sub}` | ViewerOne | `SessionChatters` + subscriber rows (`ViewerProfileDto.IsSubscriber`) | L2c |
| `random.chatter.mention` | `{random.chatter.mention}` | @ViewerOne | `ChannelContext.SessionChatters` | L2c |
| `random.quote` | `{random.quote}` | #12 "that was close" (ViewerTwo, 2026-03-01) | `IQuoteService.GetRandomAsync` | L2c |
| `quote:<n>` | `{quote:12}` | #12 "that was close" | `IQuoteService.GetAsync` | L2c |
| `quote.text:<n>` | `{quote.text:12}` | that was close | `QuoteDto.Text` | L2c |
| `quote.author:<n>` | `{quote.author:12}` | ViewerTwo | `QuoteDto.QuotedDisplayName` | L2c |
| `quote.game:<n>` | `{quote.game:12}` | Just Chatting | `QuoteDto.ContextGame` | L2c |
| `quote.date:<n>` | `{quote.date:12}` | 2026-03-01 | `QuoteDto.QuotedAt` | L2c |
| `quote.count` | `{quote.count}` | 48 | `IQuoteService.ListAsync` total | L2c |
| `quote.latest` | `{quote.latest}` | #48 "chat is wild" | `IQuoteService.ListAsync` | L2c |
| `set:<name>=<value>` | `{set:x={user.display}}hi {x}` | hi ViewerOne | render scope (`TemplateRenderScope` variable bag) | L2c |
| `var.<name>` | `{set:x=5}{var.x}` | 5 | render scope | L2c |
| `{<name>}` after `set` | `{set:greet=hey}{greet}` | hey | render scope (a set name shadows no registered key; a clash is a save-time error) | L2c |
| `include.cmd:<name>` | `{include.cmd:hug}` | the `!hug` reply rendered for this user (renamed from `include.command` on 2026-10-27; `{command.*}` is the running command, see 7.16) | `ChannelContext.Commands` (`CachedCommand`) + `ITemplateResolver` | L2c |
| `include.builtin:<key>` | `{include.builtin:uptime}` | the reply of a built-in that only reads and that any viewer may use (`WhisperSafe`, level 0), run as the viewer who triggered the template; any other built-in prints as written | `IBuiltinCommandCatalog` + `IBuiltinCommand.ExecuteAsync` | L2c |
| `include.list:<name>\|<n>` | `{include.list:snacks\|2}` | cookies | `IPickListService` | L2c |
| `pick.inline:<a>;<b>;<c>` | `{pick.inline:a;b;c}` | a, b or c (Stoney's "pick list inline"; `;` lets `\|` stay inside items) | `NoImmediateRepeatPicker` | L2c |
| `literal:<t>` | `{literal:{user}}` | {user} (nothing inside resolves) | pure | L2c |
| `{{` / `}}` | `{{x}}` | {x} (D1) | parser | L1 |
| `\{` / `\}` | `\{x\}` | {x} | parser | L1 |
| `nothing` | `a{nothing}b` | ab (for empty branches that read clearly) | pure | L2c |
| `newline` | `a{newline}b` | a + line break (Discord, webhooks; chat replaces with a space) | pure | L2c |
| `space` | `a{space}b` | a b | pure | L2c |
| `pipe` | `a{pipe}b` | a\|b | pure | L2c |
| `zws` | `@{zws}everyone` | @​everyone (zero-width space blocks a mention) | pure | L2c |

### 7.6 `stream`, `channel`, `clip` — stream data (group 4, lane L2d)

Sources: `ChannelContext` (`server/src/NomNomzBot.Domain/Platform/Interfaces/IChannelRegistry.cs`: `IsLive`,
`WentLiveAt`, `LastStreamStartedAt`, `ViewerCount`, `CurrentTitle`, `CurrentGame`, `MessageCount`, `CommandsUsed`,
`SessionChatters`); `IStreamStatsService` (`Application/Dashboard/Services/IStreamStatsService.cs`, the
`IStreamMetric` classes in `Infrastructure/Dashboard/Stats/`: peakViewers, followers, newFollowers, subs, gifters,
selfSubs, bits, raidsIn, hypeTrain, redemptions, songRequesters, chat, topChatters, commandsRun, duration);
`IChannelAnalyticsService` (`Application/Contracts/Analytics/IChannelAnalyticsService.cs`: `ListStreamsAsync`,
`GetStreamAsync` → `StreamAnalyticsDto`, `GetSummaryAsync`, `GetTopCommandsAsync`); `IRaidHistoryReader`
(`Application/Raids/IRaidHistoryReader.cs` → `RaidStats(Count, LastAt)`); `ITwitchClipsApi`, `ITwitchChannelsApi`,
`ITwitchStreamsApi`, `ITwitchSubscriptionsApi`, `ITwitchVideosApi`, `ITwitchTeamsApi` (`Application/Contracts/Twitch/`).
Freshness: `ChannelContext` = Memory; stream metrics = Cached(30 s); Helix = Cached(60 s).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `stream.uptime.long` | `{stream.uptime.long}` | 2 hours, 15 minutes | `ChannelContext.WentLiveAt` + Humanizer | L2d |
| `stream.uptime.clock` | `{stream.uptime.clock}` | 2:15:00 | `ChannelContext.WentLiveAt` | L2d |
| `stream.uptime.minutes` | `{stream.uptime.minutes}` | 135 | `ChannelContext.WentLiveAt` | L2d |
| `stream.uptime.hours` | `{stream.uptime.hours}` | 2.25 | `ChannelContext.WentLiveAt` | L2d |
| `stream.uptime.since` | `{stream.uptime.since}` | live since 13:15 | `WentLiveAt` + channel zone | L2d |
| `stream.startedAt.local` | `{stream.startedAt.local}` | 13:15 | `WentLiveAt` + zone | L2d |
| `stream.startedAt.relative` | `{stream.startedAt.relative}` | 2 hours ago | `WentLiveAt` + Humanizer | L2d |
| `stream.status` | `{stream.status}` | live | `ChannelContext.IsLive` | L2d |
| `stream.isLive` (bool) | `{if:{stream.isLive}\|on air\|off air}` | on air | `ChannelContext.IsLive` | L2d |
| `stream.id` | `{stream.id}` | 40123456789 | `ChannelContext.CurrentStreamId` | L2d |
| `stream.title.short:<n>` | `{stream.title.short:8}` | Building… | `CurrentTitle` | L2d |
| `stream.game.short` | `{stream.game.short}` | Software and Game Dev… (40 chars) | `CurrentGame` | L2d |
| `stream.game.link` | `{stream.game.link}` | https://twitch.tv/directory/category/software-and-game-development | `CurrentGame` (slug, pure) | L2d |
| `stream.game.boxart` | `{stream.game.boxart}` | https://static-cdn.jtvnw.net/ttv-boxart/…-285x380.jpg | `ITwitchGamesApi.GetGamesAsync` | L2d |
| `stream.viewers.compact` | `{stream.viewers.compact}` | 42 | `ChannelContext.ViewerCount` | L2d |
| `stream.viewers.peak` | `{stream.viewers.peak}` | 57 | `PeakViewersMetric.cs` | L2d |
| `stream.viewers.average` | `{stream.viewers.average}` | 44 | viewer-count samples (`StreamFactReader.cs`; not checked for an average field, L2d checks) | L2d |
| `stream.viewers.peakAt` | `{stream.viewers.peakAt}` | 14:05 | `PeakViewersMetric.cs` (time: not checked) | L2d |
| `stream.followers.new` | `{stream.followers.new}` | 6 | `NewFollowersMetric.cs` | L2d |
| `stream.followers.newList` | `{stream.followers.newList}` | ViewerOne, ViewerTwo | `StreamStatsDto.NewFollowers` | L2d |
| `stream.followers.latest` | `{stream.followers.latest}` | ViewerTwo | `StreamStatsDto.NewFollowers` (last) | L2d |
| `stream.subs.new` | `{stream.subs.new}` | 3 | `SubsMetric.cs` (`new`) | L2d |
| `stream.subs.resubs` | `{stream.subs.resubs}` | 2 | `SubsMetric.cs` (`resub`) | L2d |
| `stream.subs.gifted` | `{stream.subs.gifted}` | 5 | `SubsMetric.cs` (`gift`) | L2d |
| `stream.subs.total` | `{stream.subs.total}` | 10 | `SubsMetric.cs` (`total`) | L2d |
| `stream.subs.topGifter` | `{stream.subs.topGifter}` | ViewerTwo | `GiftersMetric.cs` | L2d |
| `stream.subs.selfSubs` | `{stream.subs.selfSubs}` | 1 | `SelfSubsMetric.cs` | L2d |
| `stream.bits.total` | `{stream.bits.total}` | 1,250 | `BitsMetric.cs` | L2d |
| `stream.bits.top` | `{stream.bits.top}` | ViewerOne (500) | `BitsMetric.cs` (`top`) | L2d |
| `stream.raids.in` | `{stream.raids.in}` | 1 | `RaidsInMetric.cs` | L2d |
| `stream.raids.lastRaider` | `{stream.raids.lastRaider}` | OtherStreamer | `RaidsInMetric.cs` / `IRaidHistoryReader` | L2d |
| `stream.raids.lastRaiderViewers` | `{stream.raids.lastRaiderViewers}` | 25 | `RaidsInMetric.cs` (`viewers`) | L2d |
| `stream.hypetrains` | `{stream.hypetrains}` | 1 | `HypeTrainMetric.cs` (`trains`) | L2d |
| `stream.hypetrains.topLevel` | `{stream.hypetrains.topLevel}` | 3 | `HypeTrainMetric.cs` (`level`) | L2d |
| `stream.redemptions` | `{stream.redemptions}` | 14 | `RedemptionsMetric.cs` | L2d |
| `stream.songRequesters` | `{stream.songRequesters}` | 4 | `SongRequestersMetric.cs` | L2d |
| `stream.messages` | `{stream.messages}` | 2,430 | `ChatMetric.cs` (`messages`) | L2d |
| `stream.chatters.unique` | `{stream.chatters.unique}` | 33 | `ChatMetric.cs` (`uniqueChatters`) | L2d |
| `stream.commandsRun` | `{stream.commandsRun}` | 88 | `CommandsRunMetric.cs` | L2d |
| `stream.topChatter` | `{stream.topChatter}` | ViewerOne (140) | `TopChattersMetric.cs` | L2d |
| `stream.topChatters:<n>` | `{stream.topChatters:3}` | ViewerOne, ViewerTwo, ViewerThree | `TopChattersMetric.cs` | L2d |
| `stream.clips.today` | `{stream.clips.today}` | 2 | `ITwitchClipsApi.GetClipsByBroadcasterAsync` (started_at = start of the local day) | L2d |
| `stream.clips.thisStream` | `{stream.clips.thisStream}` | 1 | `GetClipsByBroadcasterAsync` (started_at = `WentLiveAt`) | L2d |
| `clip.latest` | `{clip.latest}` | https://clips.twitch.tv/SampleClip | `GetClipsByBroadcasterAsync` | L2d |
| `clip.latest.title` | `{clip.latest.title}` | huge play | `GetClipsByBroadcasterAsync` | L2d |
| `clip.latest.creator` | `{clip.latest.creator}` | ViewerTwo | `GetClipsByBroadcasterAsync` | L2d |
| `clip.top.week` | `{clip.top.week}` | https://clips.twitch.tv/TopClip | `GetClipsByBroadcasterAsync` (7 days, by views) | L2d |
| `clip.random` | `{clip.random}` | one clip URL of the last 30 days | `GetClipsByBroadcasterAsync` | L2d |
| `stream.last.endedAt` | `{stream.last.endedAt}` | 2026-10-24 | `IChannelAnalyticsService.ListStreamsAsync` | L2d |
| `stream.last.since` | `{stream.last.since}` | 1 day, 16 hours | `ListStreamsAsync` (`EndedAt`) | L2d |
| `stream.last.relative` | `{stream.last.relative}` | 2 days ago | `ListStreamsAsync` + Humanizer | L2d |
| `stream.last.duration` | `{stream.last.duration}` | 4h 10m | `StreamAnalyticsDto.DurationSeconds` | L2d |
| `stream.last.game` | `{stream.last.game}` | Just Chatting | `StreamAnalyticsDto.GameName` | L2d |
| `stream.last.title` | `{stream.last.title}` | Chill Saturday | `StreamAnalyticsDto.Title` | L2d |
| `stream.last.peak` | `{stream.last.peak}` | 61 | `StreamAnalyticsDto.PeakViewers` | L2d |
| `stream.last.followers` | `{stream.last.followers}` | 9 | `StreamAnalyticsDto.NewFollowers` | L2d |
| `stream.last.subs` | `{stream.last.subs}` | 4 | `StreamAnalyticsDto.NewSubscribers` | L2d |
| `stream.last.messages` | `{stream.last.messages}` | 3,102 | `StreamAnalyticsDto.TotalMessages` | L2d |
| `stream.games.history:<n>` | `{stream.games.history:3}` | Software and Game Development, Just Chatting, Minecraft | `ListStreamsAsync` (`GameName`, newest first, distinct) | L2d |
| `stream.games.thisStream` | `{stream.games.thisStream}` | Just Chatting → Software and Game Development | category changes this stream (event journal `channel.update`; not checked for a reader, L2d checks) | L2d |
| `stream.count` | `{stream.count}` | 214 | `ListStreamsAsync` total | L2d |
| `stream.count.month` | `{stream.count.month}` | 11 | `ListStreamsAsync` (this month) | L2d |
| `stream.hours.month` | `{stream.hours.month}` | 46h | `IChannelAnalyticsService.GetDailySeriesAsync` | L2d |
| `stream.average.duration` | `{stream.average.duration}` | 3h 50m | `GetSummaryAsync` (field: not checked) | L2d |
| `stream.number` | `{stream.number}` | stream #214 | `ListStreamsAsync` total | L2d |
| `stream.topCommand` | `{stream.topCommand}` | !hug (31) | `IChannelAnalyticsService.GetTopCommandsAsync` | L2d |
| `stream.marker` | `{stream.marker}` | marker set at 2:15:00 | `ITwitchStreamsApi.CreateStreamMarkerAsync` — **action, not a read: CUT** (see 7.20) | L2d |
| `channel.login` | `{channel.login}` | samplechannel | `ChannelContext.ChannelName` | L2d |
| `channel.mention` | `{channel.mention}` | @SampleChannel | `ChannelContext.DisplayName` | L2d |
| `channel.url` (all contexts) | `{channel.url}` | https://twitch.tv/samplechannel | `ChannelName` (pure) | L2d |
| `channel.followers` | `{channel.followers}` | 1,234 | `ITwitchChannelsApi.GetChannelFollowerCountAsync` | L2d |
| `channel.followers.compact` | `{channel.followers.compact}` | 1.2k | same | L2d |
| `channel.subs` | `{channel.subs}` | 80 | `ITwitchSubscriptionsApi.GetSubscriberCountAsync` | L2d |
| `channel.subPoints` | `{channel.subPoints}` | 92 | `GetSubscriberCountAsync` (points: not checked) | L2d |
| `channel.language` | `{channel.language}` | en | `ChannelContext.Language` | L2d |
| `channel.timezone` | `{channel.timezone}` | Europe/Amsterdam | `ChannelContext.Timezone` | L2d |
| `channel.localTime` | `{channel.localTime}` | 15:30 | `TimeProvider` + `Timezone` | L2d |
| `channel.personality` | `{channel.personality}` | Informative | `ChannelContext.Personality` | L2d |
| `channel.prefix` | `{channel.prefix}` | ! | `ChannelContext.CommandPrefix` | L2d |
| `channel.tags` | `{channel.tags}` | English, Coding | `ITwitchChannelsApi.GetChannelInformationAsync` | L2d |
| `channel.ccls` | `{channel.ccls}` | (empty: none) | `GetChannelInformationAsync` (content labels) | L2d |
| `channel.isBranded` | `{if:{channel.isBranded}\|#ad\|}` | (empty) | `GetChannelInformationAsync` | L2d |
| `channel.team` | `{channel.team}` | SampleTeam | `ITwitchTeamsApi.GetChannelTeamsAsync` | L2d |
| `channel.editors.count` | `{channel.editors.count}` | 2 | `ITwitchChannelsApi.GetChannelEditorsAsync` | L2d |
| `channel.mods.count` | `{channel.mods.count}` | 6 | `ITwitchModeratorsApi.GetModeratorsAsync` | L2d |
| `channel.vips.count` | `{channel.vips.count}` | 4 | `ITwitchModeratorsApi.GetVipsAsync` | L2d |
| `channel.vod.latest` | `{channel.vod.latest}` | https://www.twitch.tv/videos/123 | `ITwitchVideosApi.GetVideosByBroadcasterAsync` | L2d |
| `channel.vod.latest.title` | `{channel.vod.latest.title}` | Chill Saturday | `GetVideosByBroadcasterAsync` | L2d |
| `channel.raids.outCount:<login>` | `{channel.raids.outCount:otherstreamer}` | 3 | `IRaidHistoryReader` (`Outgoing`) | L2d |
| `channel.raids.inCount:<login>` | `{channel.raids.inCount:otherstreamer}` | 5 | `IRaidHistoryReader` (`Incoming`) | L2d |
| `channel.raids.lastOut` | `{channel.raids.lastOut}` | OtherStreamer | `IRaidHistoryReader` | L2d |
| `channel.raids.lastIn.at` | `{channel.raids.lastIn.at}` | 2026-10-24 | `RaidStats.LastAt` | L2d |
| `channel.age` | `{channel.age}` | 6 years, 2 months | `ITwitchUsersApi.GetUsersByIdsAsync` (`created_at`) | L2d |

### 7.7 `user`, `target`, `viewer` — viewer data (groups 5 and 15, lane L2e)

`<who>` is `user` or `target`; every `user.*` row below also exists as `target.*` (the Example shows one side, the
row count counts both sides only where both are listed). Sources: `IViewerAnalyticsService`
(`Application/Contracts/Analytics/IViewerAnalyticsService.cs` → `ViewerProfileDto`, `WatchStreakDto`,
`GetEngagementSeriesAsync`); `IViewerProfileService` (`Application/Community/Services/`); `ICurrencyAccountService`,
`IEconomyLeaderboardService` (`Application/Economy/Services/` → `LeaderboardEntryDto(Rank, …, Value)`);
`ICurrencyConfigService` (points name); `IFollowStateService` (`Application/Moderation/Services/`);
`IPronounResolutionService`; `ITwitchUsersApi`, `ITwitchChatApi.GetUserChatColorAsync`; `ChannelContext.ModerationStandings`;
`IViewerSelfActivityService` (`Application/Contracts/Viewer/`); `IViewerDataService`. Seeds give the role booleans.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `user.display` | `{user.display}` | ViewerOne | trigger seed | L2e |
| `user.login` | `{user.login}` | viewerone | trigger seed | L2e |
| `user.mention` | `{user.mention}` | @ViewerOne | trigger seed | L2e |
| `user.platform` | `{user.platform}` | twitch | trigger seed (`user.provider`) | L2e |
| `user.platform.name` | `{user.platform.name}` | Twitch | trigger seed | L2e |
| `user.color` | `{user.color}` | #1E90FF | `ITwitchChatApi.GetUserChatColorAsync` | L2e |
| `user.avatar` | `{user.avatar}` | https://static-cdn.jtvnw.net/…/viewerone.png | `ITwitchUsersApi.GetUsersByIdsAsync` | L2e |
| `user.bio` | `{user.bio}` | I like bots | `GetUsersByIdsAsync` (`description`) | L2e |
| `user.createdAt` | `{user.createdAt}` | 2022-08-14 | `GetUsersByIdsAsync` | L2e |
| `user.accountAge.days` | `{user.accountAge.days}` | 1534 | `GetUsersByIdsAsync` | L2e |
| `user.followAge.days` | `{user.followAge.days}` | 411 | `IFollowStateService` | L2e |
| `user.followedAt` | `{user.followedAt}` | 2025-09-10 | `IFollowStateService` | L2e |
| `user.isFollower` | `{if:{user.isFollower}\|thanks for the follow\|follow?}` | thanks for the follow | `ViewerProfileDto.IsFollower` | L2e |
| `user.isSub` | `{if:{user.isSub}\|sub\|}` | sub | `ViewerProfileDto.IsSubscriber` | L2e |
| `user.subTier` | `{user.subTier}` | 1 | `ViewerProfileDto.SubTier` | L2e |
| `user.subTier.name` | `{user.subTier.name}` | Tier 1 | `SubTier` | L2e |
| `user.subMonths` | `{user.subMonths}` | 7 | `ITwitchSubscriptionsApi.CheckUserSubscriptionAsync` (months: not checked; else event journal) | L2e |
| `user.isMod` | `{if:{user.isMod}\|🛡️\|}` | (empty) | role seed (spec §6.3.2 `user.ismod`) | L2e |
| `user.isVip` | `{if:{user.isVip}\|💎\|}` | (empty) | `ChannelContext.ModerationStandings` / standing seed | L2e |
| `user.isBroadcaster` | `{if:{user.isBroadcaster}\|boss\|}` | (empty) | role seed | L2e |
| `user.isFirstChat` | `{if:{user.isFirstChat}\|welcome!\|}` | (empty) | `ChannelContext.FirstChatChecked` | L2e |
| `user.isReturning` | `{if:{user.isReturning}\|welcome back\|}` | welcome back | engagement seed / `ViewerProfileDto.LastSeenAt` | L2e |
| `user.role` | `{user.role}` | subscriber | role seed (highest rung, plain word) | L2e |
| `user.badges` | `{user.badges}` | subscriber, sub-gifter | chat message seed (badges; not checked as a seed, L2e checks `ChatMessageHandler.cs`) | L2e |
| `user.pronouns.subject` | `{user.pronouns.subject}` | she | `IPronounResolutionService` (alias of `user.subject`) | L2e |
| `user.stats.watchTime.hours` | `{user.stats.watchTime.hours}` | 12.5 | `ViewerProfileDto.TotalWatchSeconds` | L2e |
| `user.stats.watchTime.long` | `{user.stats.watchTime.long}` | 12 hours, 30 minutes | same + Humanizer | L2e |
| `user.stats.watchTime.rank` | `{user.stats.watchTime.rank}` | 9 | `IChannelAnalyticsService.GetTopViewersAsync` (metric Watch) | L2e |
| `user.stats.messages.rank` | `{user.stats.messages.rank}` | 2 | `GetTopViewersAsync` (metric Messages) | L2e |
| `user.stats.messages.stream` | `{user.stats.messages.stream}` | 25 | chat messages this stream (`IChatSearchService`, from `WentLiveAt`) | L2e |
| `user.stats.messages.today` | `{user.stats.messages.today}` | 31 | `IChatSearchService` (local day) | L2e |
| `user.stats.commands.stream` | `{user.stats.commands.stream}` | 3 | command runs this stream (`CommandsRunMetric.cs` per user: not checked) | L2e |
| `user.stats.firstSeen.relative` | `{user.stats.firstSeen.relative}` | 1 year ago | `ViewerProfileDto.FirstSeenAt` | L2e |
| `user.stats.lastSeen` | `{user.stats.lastSeen}` | 2026-10-24 | `ViewerProfileDto.LastSeenAt` | L2e |
| `user.stats.lastSeen.relative` | `{user.stats.lastSeen.relative}` | 2 days ago | `LastSeenAt` | L2e |
| `user.stats.streak` | `{user.stats.streak}` | 5 | `IViewerAnalyticsService.GetStreakAsync` (`CurrentStreak`) | L2e |
| `user.stats.streak.best` | `{user.stats.streak.best}` | 9 | `WatchStreakDto.MaxStreak` | L2e |
| `user.stats.streams` | `{user.stats.streams}` | 63 | `GetEngagementSeriesAsync` (days present; not checked as per-stream) | L2e |
| `user.activity.level` | `{user.activity.level}` | veteran | how active the viewer is in this channel, from the viewer's real message count (`ViewerProfileDto.TotalMessages`) against the channel's own steps: `new` below `ViewerCasualFrom` (default 10), `casual` from there, `regular` from `ViewerRegularFrom` (default 100), `veteran` from `ViewerVeteranFrom` (default 500). The steps are three channel settings on the channel basics read and write (`GET`/`PUT /channels/{id}/settings/basics`); a viewer with no profile has no messages and reads `new`. The sample is ViewerOne with 1204 messages | L2e |
| `user.stats.bits` | `{user.stats.bits}` | 2,300 | `ViewerProfileDto.TotalBitsCheered` | L2e |
| `user.stats.redemptions.last` | `{user.stats.redemptions.last}` | Hydrate | redemptions table (`IViewerSelfActivityService`; field: not checked) | L2e |
| `user.stats.redemptions.lastAt` | `{user.stats.redemptions.lastAt}` | 20 minutes ago | same | L2e |
| `user.stats.songRequests.stream` | `{user.stats.songRequests.stream}` | 2 | `IMusicHistoryReader` (this stream) | L2e |
| `user.points` | `{user.points}` | 2,500 | `ICurrencyAccountService.GetBalanceAsync` | L2e |
| `user.points.raw` | `{user.points.raw}` | 2500 | `GetBalanceAsync` | L2e |
| `user.points.name` | `{user.points} {user.points.name}` | 2,500 nomz | `ICurrencyConfigService` (channel points name) | L2e |
| `user.points.rank` | `{user.points.rank}` | 4 | `IEconomyLeaderboardService.GetRankingAsync` (`Rank`) | L2e |
| `user.points.rank.ordinal` | `{user.points.rank.ordinal}` | 4th | same + Humanizer | L2e |
| `user.points.nextRank.gap` | `{user.points.nextRank.gap}` | 300 | `GetRankingAsync` (rank 3 value minus own) | L2e |
| `user.points.nextRank.name` | `{user.points.nextRank.name}` | ViewerThree | `GetRankingAsync` | L2e |
| `user.points.top.percent` | `{user.points.top.percent}` | top 5% | `GetRankingAsync` + `ListAccountsAsync` total | L2e |
| `user.points.earnedToday` | `{user.points.earnedToday}` | 120 | `ICurrencyAccountService.GetLedgerAsync` (today) | L2e |
| `user.points.isFrozen` | `{if:{user.points.isFrozen}\|wallet frozen\|}` | (empty) | `CurrencyAccountDto` | L2e |
| `user.rank:<board>` | `{user.rank:watchtime}` | 9 | `IEconomyLeaderboardService.GetRankingAsync` (a configured board) | L2e |
| `user.rank.value:<board>` | `{user.rank.value:watchtime}` | 45000 | same | L2e |
| `user.data.<key>` | `{user.data.color}` | blue | `IViewerDataService` | L2e |
| `user.data.<key>:<default>` | `{user.data.color:none}` | blue | `IViewerDataService` | L2e |
| `user.quotes.count` | `{user.quotes.count}` | 3 | `IQuoteService.ListByUserAsync` | L2e |
| `user.giveaways.entered` | `{if:{user.giveaways.entered}\|you're in\|type !join}` | you're in | `IGiveawayService` (entry check: not checked) | L2e |
| `user.queue.position` | `{user.queue.position}` | 3 | `IViewerSelfQueueService` | L2e |
| `user.lastMessage.at` | `{user.lastMessage.at}` | 1 minute ago | `ResolveLastMessagesAsync` source | L2e |
| `user.randomMessage` | `{user.randomMessage}` | hello there | `ResolveLastMessagesAsync` source | L2e |
| `user.shoutoutLine` | `{user.shoutoutLine}` | — **CUT**: a shout-out text is the builtin's own reply, not a value | — | L2e |
| `target.mention` | `{target.mention}` | @ViewerTwo | resolved target | L2e |
| `target.isFollower` | `{if:{target.isFollower}\|follows\|does not follow}` | follows | `IFollowStateService` | L2e |
| `target.isSub` | `{if:{target.isSub}\|sub\|not a sub}` | not a sub | subscriber rows | L2e |
| `target.isLive` | `{if:{target.isLive}\|live now\|offline}` | offline | `ITwitchStreamsApi.GetStreamsAsync` (shout-out seed `target.isLive` as-built) | L2e |
| `target.game` | `{target.game}` | Minecraft | `ITwitchChannelsApi.GetChannelInformationByTwitchIdsAsync` | L2e |
| `target.title` | `{target.title}` | Building castles | same | L2e |
| `target.viewers` | `{target.viewers}` | 18 | `ITwitchStreamsApi.GetStreamsAsync` | L2e |
| `target.followers` | `{target.followers}` | 950 | `GetChannelFollowerCountAsync` (by target id) | L2e |
| `target.avatar` | `{target.avatar}` | https://static-cdn.jtvnw.net/…/viewertwo.png | `GetUsersByIdsAsync` | L2e |
| `target.bio` | `{target.bio}` | I build castles | `GetUsersByIdsAsync` | L2e |
| `target.createdAt` | `{target.createdAt}` | 2019-05-02 | `GetUsersByIdsAsync` | L2e |
| `target.accountAge` | `{target.accountAge}` | 7 years, 5 months | `GetUsersByIdsAsync` | L2e |
| `target.lastStream.game` | `{target.lastStream.game}` | Minecraft | `GetChannelInformationByTwitchIdsAsync` (last category set) | L2e |
| `target.lastStream.at` | `{target.lastStream.at}` | 3 days ago | `ITwitchVideosApi.GetVideosByTwitchUserIdAsync` (latest archive) | L2e |
| `target.team` | `{target.team}` | SampleTeam | `ITwitchTeamsApi.GetChannelTeamsAsync` | L2e |
| `target.points` | `{target.points}` | 800 | `ICurrencyAccountService.GetBalanceAsync` | L2e |
| `target.points.rank` | `{target.points.rank}` | 17 | `IEconomyLeaderboardService` | L2e |
| `target.stats.streak` | `{target.stats.streak}` | 2 | `IViewerAnalyticsService.GetStreakAsync` | L2e |
| `target.stats.lastSeen` | `{target.stats.lastSeen}` | 2026-10-25 | `ViewerProfileDto.LastSeenAt` | L2e |
| `target.activity.level` | `{target.activity.level}` | veteran | same level for the @mentioned viewer, with the same channel steps (ViewerTwo also has 1204 messages in the sample data) | L2e |
| `target.stats.bits` | `{target.stats.bits}` | 0 | `ViewerProfileDto.TotalBitsCheered` | L2e |
| `target.pronouns` | `{target.pronouns}` | he/him | `IPronounResolutionService` | L2e |
| `target.color` | `{target.color}` | #FF4500 | `GetUserChatColorAsync` | L2e |
| `target.role` | `{target.role}` | viewer | standings + subscriber rows | L2e |
| `target.isMod` | `{if:{target.isMod}\|mod\|}` | (empty) | `ITwitchModeratorsApi.GetModeratorsAsync` (cached list) | L2e |
| `target.isVip` | `{if:{target.isVip}\|vip\|}` | (empty) | `ITwitchModeratorsApi.GetVipsAsync` | L2e |
| `target.platform` | `{target.platform}` | twitch | resolved target | L2e |
| `target.shoutoutLink` | `{target.shoutoutLink}` | https://twitch.tv/viewertwo | resolved target (pure) | L2e |
| `args.<n>.user.<key>` | `{args.1.user.followAge}` | 9 months | the n-th argument resolved as a user, then any `user.*` key (Stoney's group 15) | L2e |
| `args.<n>.user.display` | `{args.1.user.display}` | ViewerTwo | same | L2e |
| `args.<n>.user.points` | `{args.1.user.points}` | 800 | same + `ICurrencyAccountService` | L2e |
| `args.<n>.user.game` | `{args.1.user.game}` | Minecraft | same + `GetChannelInformationByTwitchIdsAsync` | L2e |

### 7.8 `count`, `list` — counters and lists (group 6, lane L2f)

Sources: `INamedCounterService` (`Application/Commands/Services/INamedCounterService.cs`: `SetAsync`, `AdjustAsync`
returning the new `long`); `IPickListService` (`Application/PickLists/Services/`). A write form (`:+1`, `:set`)
renders the value **after** the write. A write runs once per render even when the token appears twice (the plan
step 3 dedupes write nodes). Counter "since stream start" keeps a snapshot of the counter at `stream.online`
(new per-channel row; L2f adds it to both migration sets).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `count.<key>:+<n>` | `{count.deaths:+1}` | 13 | `INamedCounterService.AdjustAsync` | L2f |
| `count.<key>:-<n>` | `{count.deaths:-1}` | 11 | `AdjustAsync` | L2f |
| `count.<key>:set=<n>` | `{count.deaths:set=0}` | 0 | `INamedCounterService.SetAsync` | L2f |
| `count.<key>:reset` | `{count.deaths:reset}` | 0 | `SetAsync` | L2f |
| `count.<key>:+<n>\|max=<m>` | `{count.wins:+1\|max=3}` | 3 | `AdjustAsync` + clamp | L2f |
| `count.<key>.stream` | `{count.deaths.stream}` | 4 | stream-start snapshot + current value | L2f |
| `count.<key>.formatted` | `{count.deaths.formatted}` | 12 | `INamedCounterService` + channel culture | L2f |
| `count.<key>.ordinal` | `{count.deaths.ordinal}` | 12th | same + Humanizer | L2f |
| `count.<key>.words` | `{count.deaths.words}` | twelve | same + Humanizer | L2f |
| `count.<key>.perHour` | `{count.deaths.perHour}` | 1.8 | stream snapshot + uptime | L2f |
| `count.<key>.percentOf:<other>` | `{count.wins.percentOf:games}` | 25% | two counters | L2f |
| `count.<key>.ratio:<other>` | `{count.wins.ratio:deaths}` | 0.25 | two counters | L2f |
| `count.<key>.user` | `{count.hugs.user}` | 5 (this viewer's own count) | `IViewerDataService` key `count.hugs` (per-viewer counter) | L2f |
| `count.<key>.user:+1` | `{count.hugs.user:+1}` | 6 | `IViewerDataService` | L2f |
| `count.<key>.target:+1` | `{count.hugs.target:+1}` | 2 (hugs ViewerTwo got) | `IViewerDataService` (target side) | L2f |
| `count.<key>.top` | `{count.hugs.top}` | ViewerOne (5) | `IViewerDataService` (top value for key; query: not checked) | L2f |
| `count.list` | `{count.list}` | deaths 12, wins 3 | `INamedCounterService` (list: not checked; else counters table) | L2f |
| `list.pick:<name>` | `{list.pick:snacks}` | chips, cookies or fruit | `IPickListService` | L2f |
| `list.length:<name>` | `{list.length:snacks}` | 3 | `IPickListService` | L2f |
| `list.item:<name>\|<n>` | `{list.item:snacks\|2}` | cookies | `IPickListService` | L2f |
| `list.first:<name>` | `{list.first:snacks}` | chips | `IPickListService` | L2f |
| `list.last:<name>` | `{list.last:snacks}` | fruit | `IPickListService` | L2f |
| `list.join:<name>[\|<sep>]` | `{list.join:snacks\| / }` | chips / cookies / fruit | `IPickListService` | L2f |
| `list.join.and:<name>` | `{list.join.and:snacks}` | chips, cookies and fruit | `IPickListService` | L2f |
| `list.pick.n:<name>\|<n>` | `{list.pick.n:snacks\|2}` | 2 of chips, cookies, fruit | `IPickListService` | L2f |
| `list.shuffle:<name>` | `{list.shuffle:snacks}` | a permutation, joined with ", " | `IPickListService` | L2f |
| `list.sorted:<name>` | `{list.sorted:snacks}` | chips, cookies, fruit | `IPickListService` | L2f |
| `list.contains:<name>\|<v>` | `{if:{list.contains:snacks\|chips}\|yes\|no}` | yes | `IPickListService` | L2f |
| `list.indexOf:<name>\|<v>` | `{list.indexOf:snacks\|fruit}` | 3 | `IPickListService` | L2f |
| `list.random.index:<name>` | `{list.random.index:snacks}` | 1..3 | `IPickListService` | L2f |
| `list.cycle:<name>` | `{list.cycle:snacks}` | chips, then cookies, then fruit on the next renders | `IPickListService` + a cursor in `IViewerDataService`-style channel store (new key; not checked) | L2f |
| `list.inline.length:<a>;<b>` | `{list.inline.length:a;b;c}` | 3 | pure | L2f |
| `list.inline.item:<n>\|<a>;<b>` | `{list.inline.item:2\|a;b;c}` | b | pure | L2f |
| `list.inline.join:<sep>\|<a>;<b>` | `{list.inline.join: + \|a;b}` | a + b | pure | L2f |
| `list.count` | `{list.count}` | 7 (pick lists in the channel) | `IPickListService` | L2f |

### 7.9 `music` — music and song requests (group 8, lane L2g)

Sources: `IMusicService` (`Application/Music/Services/IMusicService.cs`: `GetNowPlayingAsync` → `NowPlaying(TrackName,
Artist, Album, ImageUrl, DurationMs, ProgressMs, IsPlaying, Volume, RequestedBy, Provider, TrackUri, ShuffleEnabled,
RepeatMode…)`, `GetQueueAsync` → `MusicQueue(CurrentTrack, Queue)` with `MusicQueueItem(TrackName, Artist,
ImageUrl, DurationMs, RequestedBy, Cost, Code…)`, `SnapshotQueue` → `SongRequestQueueSnapshotItem(Title,
RequestedBy, DurationSec, Number, Code)`, `GetActiveProviderKeyAsync`); `IMusicHistoryReader`;
`IPlaybackStateReader` (`Domain/Music/Interfaces/`); `IMusicConfigService`. Freshness: Memory (the queue snapshot) or
Cached(5 s) for now-playing.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `music.now` | `{music.now}` | Song A by Artist A | `NowPlaying.TrackName` + `.Artist` | L2g |
| `music.now.title` | `{music.now.title}` | Song A | `NowPlaying.TrackName` | L2g |
| `music.now.artist` | `{music.now.artist}` | Artist A | `NowPlaying.Artist` | L2g |
| `music.now.album` | `{music.now.album}` | Album A | `NowPlaying.Album` | L2g |
| `music.now.cover` | `{music.now.cover}` | https://i.scdn.co/image/sample | `NowPlaying.ImageUrl` | L2g |
| `music.now.link` | `{music.now.link}` | https://open.spotify.com/track/sample | `NowPlaying.TrackUri` (provider link, pure) | L2g |
| `music.now.requester` | `{music.now.requester}` | ViewerTwo | `NowPlaying.RequestedBy` | L2g |
| `music.now.requester.mention` | `{music.now.requester.mention}` | @ViewerTwo | same | L2g |
| `music.now.duration` | `{music.now.duration}` | 3:30 | `NowPlaying.DurationMs` | L2g |
| `music.now.progress` | `{music.now.progress}` | 1:23 | `NowPlaying.ProgressMs` | L2g |
| `music.now.remaining` | `{music.now.remaining}` | 2:07 | `DurationMs - ProgressMs` | L2g |
| `music.now.percent` | `{music.now.percent}` | 40% | same | L2g |
| `music.now.bar` | `{music.now.bar}` | ▬▬▬▬🔘▬▬▬▬▬▬ 1:23 / 3:30 | same (pure bar) | L2g |
| `music.now.bar:<width>` | `{music.now.bar:5}` | ▬▬🔘▬▬ | same | L2g |
| `music.now.isPlaying` | `{if:{music.now.isPlaying}\|▶️\|⏸️}` | ▶️ | `NowPlaying.IsPlaying` | L2g |
| `music.now.status` | `{music.now.status}` | playing | `IPlaybackStateReader.StateOf` | L2g |
| `music.volume` | `{music.volume}` | 60% | `NowPlaying.Volume` | L2g |
| `music.shuffle` | `{music.shuffle}` | off | `NowPlaying.ShuffleEnabled` | L2g |
| `music.repeat` | `{music.repeat}` | off | `NowPlaying.RepeatMode` | L2g |
| `music.provider` | `{music.provider}` | Spotify | `IMusicService.GetActiveProviderKeyAsync` | L2g |
| `music.queue.length` | `{music.queue.length}` | 5 | `MusicQueue.Queue.Count` | L2g |
| `music.queue.duration` | `{music.queue.duration}` | 17m 20s | sum of `DurationMs` | L2g |
| `music.queue.next` | `{music.queue.next}` | Song B by Artist B | `Queue[0]` | L2g |
| `music.queue.next.requester` | `{music.queue.next.requester}` | ViewerThree | `Queue[0].RequestedBy` | L2g |
| `music.queue.item:<n>` | `{music.queue.item:2}` | Song C by Artist C | `Queue[n-1]` | L2g |
| `music.queue.list:<n>` | `{music.queue.list:3}` | 1. Song B, 2. Song C, 3. Song D | `Queue` | L2g |
| `music.queue.requesters` | `{music.queue.requesters}` | 4 | distinct `RequestedBy` | L2g |
| `music.queue.isEmpty` | `{if:{music.queue.isEmpty}\|queue is empty\|{music.queue.length} songs}` | 5 songs | `Queue.Count` | L2g |
| `music.queue.position` | `{music.queue.position}` | 3 (the user's next song) | `SnapshotQueue` (`RequestedBy`, `Number`) | L2g |
| `music.queue.position.ordinal` | `{music.queue.position.ordinal}` | 3rd | same | L2g |
| `music.queue.wait` | `{music.queue.wait}` | 7m 40s | remaining now-playing + `DurationSec` before the user's item | L2g |
| `music.queue.wait.at` | `{music.queue.wait.at}` | about 15:38 | same + clock + zone | L2g |
| `music.queue.mine.count` | `{music.queue.mine.count}` | 2 | `SnapshotQueue` by `RequestedBy` | L2g |
| `music.queue.mine.titles` | `{music.queue.mine.titles}` | Song C, Song E | same | L2g |
| `music.queue.mine.code` | `{music.queue.mine.code}` | K7QM | `SongRequestQueueSnapshotItem.Code` | L2g |
| `music.queue.mine.next` | `{music.queue.mine.next}` | Song C | same | L2g |
| `music.queue.target.count` | `{music.queue.target.count}` | 1 | `SnapshotQueue` by target | L2g |
| `music.queue.cost.total` | `{music.queue.cost.total}` | 1,500 | `MusicQueueItem.Cost` | L2g |
| `music.history.last` | `{music.history.last}` | Song Z by Artist Z | `IMusicHistoryReader` | L2g |
| `music.history.list:<n>` | `{music.history.list:3}` | Song Z, Song Y, Song X | `IMusicHistoryReader` | L2g |
| `music.history.count.stream` | `{music.history.count.stream}` | 21 | `IMusicHistoryReader` (since `WentLiveAt`) | L2g |
| `music.history.topRequester.stream` | `{music.history.topRequester.stream}` | ViewerTwo (4) | `IMusicHistoryReader` | L2g |
| `music.history.topArtist.stream` | `{music.history.topArtist.stream}` | Artist A | `IMusicHistoryReader` | L2g |
| `music.user.requests.total` | `{music.user.requests.total}` | 9 | `ViewerProfileDto.TotalSongRequests` | L2g |
| `music.user.requests.stream` | `{music.user.requests.stream}` | 2 | `IMusicHistoryReader` | L2g |
| `music.user.lastRequest` | `{music.user.lastRequest}` | Song C | `IMusicHistoryReader` / `SnapshotQueue` | L2g |
| `music.requests.open` | `{if:{music.requests.open}\|requests are open\|requests are closed}` | requests are open | `IMusicConfigService` (field: not checked) | L2g |
| `music.requests.cost` | `{music.requests.cost}` | 500 | `IMusicConfigService` (field: not checked) | L2g |
| `music.requests.maxPerUser` | `{music.requests.maxPerUser}` | 3 | `IMusicConfigService` (field: not checked) | L2g |
| `music.requests.maxDuration` | `{music.requests.maxDuration}` | 6:00 | `IMusicConfigService` (field: not checked) | L2g |
| `music.page` | `{music.page}` | https://nomnomz.bot/sr/samplechannel | song request page (`ISongRequestPageTokenService`; URL shape: not checked) | L2g |

### 7.10 `poll`, `prediction`, `hypetrain`, `goal`, `charity` — live events (group 11, lane L2h)

These read the **current** state any time (a command `!poll`), unlike the as-built `event.poll.*` seeds that exist
only inside the event. Sources: `ITwitchPollsApi.GetPollsAsync`, `ITwitchPredictionsApi.GetPredictionsAsync`,
`ITwitchHypeTrainApi.GetHypeTrainStatusAsync`, `ITwitchGoalsApi.GetCreatorGoalsAsync`,
`ITwitchCharityApi.GetCharityCampaignAsync` (`Application/Contracts/Twitch/`); the bot's own chat polls
`IChatPollService` (`Application/Community/Services/IChatPollService.cs` → `ChatPollDto(Question, Options, Status,
TotalVotes, OpenedAt, ClosesAt…)`) and `ChannelContext.ActiveChatPoll`; history `IPollPredictionHistoryService`.
Freshness: Cached(10 s), refreshed by the EventSub begin/progress/end events.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `poll.question` | `{poll.question}` | Best snack? | `ITwitchPollsApi.GetPollsAsync` (active) | L2h |
| `poll.isActive` | `{if:{poll.isActive}\|vote now!\|no poll}` | vote now! | `GetPollsAsync` | L2h |
| `poll.options` | `{poll.options}` | chips, cookies | `GetPollsAsync` | L2h |
| `poll.options.numbered` | `{poll.options.numbered}` | 1. chips 2. cookies | `GetPollsAsync` | L2h |
| `poll.leading` | `{poll.leading}` | chips | `GetPollsAsync` (max votes) | L2h |
| `poll.leading.votes` | `{poll.leading.votes}` | 12 | `GetPollsAsync` | L2h |
| `poll.leading.percent` | `{poll.leading.percent}` | 60% | `GetPollsAsync` | L2h |
| `poll.votes.total` | `{poll.votes.total}` | 20 | `GetPollsAsync` | L2h |
| `poll.option:<n>` | `{poll.option:2}` | cookies | `GetPollsAsync` | L2h |
| `poll.option.votes:<n>` | `{poll.option.votes:2}` | 8 | `GetPollsAsync` | L2h |
| `poll.option.percent:<n>` | `{poll.option.percent:2}` | 40% | `GetPollsAsync` | L2h |
| `poll.results` (live) | `{poll.results}` | chips 60%, cookies 40% | `GetPollsAsync` | L2h |
| `poll.results.bars` | `{poll.results.bars}` | chips ██████░░░░ 60% · cookies ████░░░░░░ 40% | `GetPollsAsync` + meter | L2h |
| `poll.timeLeft` | `{poll.timeLeft}` | 1m 30s | `GetPollsAsync` (`started_at` + `duration`) | L2h |
| `poll.endsAt` | `{poll.endsAt}` | 15:32 | same + zone | L2h |
| `poll.isTie` | `{if:{poll.isTie}\|it's a tie\|{poll.leading} leads}` | chips leads | `GetPollsAsync` | L2h |
| `poll.last.winner` | `{poll.last.winner}` | chips | `IPollPredictionHistoryService.ListAsync` | L2h |
| `poll.last.question` | `{poll.last.question}` | Best snack? | `IPollPredictionHistoryService` | L2h |
| `chatpoll.question` | `{chatpoll.question}` | Next game? | `ChannelContext.ActiveChatPoll` / `IChatPollService.GetAsync` | L2h |
| `chatpoll.options` | `{chatpoll.options}` | 1 Minecraft, 2 Tetris | `ChatPollDto.Options` | L2h |
| `chatpoll.leading` | `{chatpoll.leading}` | Minecraft | `ChatPollDto.Options` | L2h |
| `chatpoll.leading.percent` | `{chatpoll.leading.percent}` | 55% | `ChatPollDto` | L2h |
| `chatpoll.votes.total` | `{chatpoll.votes.total}` | 22 | `ChatPollDto.TotalVotes` | L2h |
| `chatpoll.timeLeft` | `{chatpoll.timeLeft}` | 45s | `ChatPollDto.ClosesAt` | L2h |
| `chatpoll.howToVote` | `{chatpoll.howToVote}` | type 1 or 2 | `ChatPollDto.Options` (count) | L2h |
| `prediction.question` | `{prediction.question}` | Win? | `ITwitchPredictionsApi.GetPredictionsAsync` | L2h |
| `prediction.isActive` | `{if:{prediction.isActive}\|bet now\|}` | bet now | `GetPredictionsAsync` | L2h |
| `prediction.status.now` | `{prediction.status.now}` | open | `GetPredictionsAsync` | L2h |
| `prediction.outcomes.list` | `{prediction.outcomes.list}` | Yes, No | `GetPredictionsAsync` | L2h |
| `prediction.leading` | `{prediction.leading}` | Yes | `GetPredictionsAsync` (most points) | L2h |
| `prediction.leading.points` | `{prediction.leading.points}` | 3,000 | `GetPredictionsAsync` | L2h |
| `prediction.leading.percent` | `{prediction.leading.percent}` | 75% | `GetPredictionsAsync` | L2h |
| `prediction.points.total` | `{prediction.points.total}` | 4,000 | `GetPredictionsAsync` | L2h |
| `prediction.users.total` | `{prediction.users.total}` | 12 | `GetPredictionsAsync` (`users` per outcome) | L2h |
| `prediction.outcome:<n>` | `{prediction.outcome:2}` | No | `GetPredictionsAsync` | L2h |
| `prediction.outcome.points:<n>` | `{prediction.outcome.points:2}` | 1,000 | `GetPredictionsAsync` | L2h |
| `prediction.outcome.percent:<n>` | `{prediction.outcome.percent:2}` | 25% | `GetPredictionsAsync` | L2h |
| `prediction.outcome.odds:<n>` | `{prediction.outcome.odds:2}` | 1:4 | `GetPredictionsAsync` (pure ratio) | L2h |
| `prediction.topPredictor` | `{prediction.topPredictor}` | ViewerOne (1,000) | `GetPredictionsAsync` (`top_predictors`) | L2h |
| `prediction.timeLeft` | `{prediction.timeLeft}` | 45s | `GetPredictionsAsync` (`created_at` + window) | L2h |
| `prediction.last.winner` | `{prediction.last.winner}` | Yes | `IPollPredictionHistoryService` | L2h |
| `hypetrain.isActive` | `{if:{hypetrain.isActive}\|🚂 choo choo\|}` | 🚂 choo choo | `ITwitchHypeTrainApi.GetHypeTrainStatusAsync` | L2h |
| `hypetrain.level` | `{hypetrain.level}` | 3 | `GetHypeTrainStatusAsync` | L2h |
| `hypetrain.progress` | `{hypetrain.progress}` | 1,800 | `GetHypeTrainStatusAsync` | L2h |
| `hypetrain.goal` | `{hypetrain.goal}` | 3,000 | `GetHypeTrainStatusAsync` | L2h |
| `hypetrain.percent` | `{hypetrain.percent}` | 60% | progress / goal | L2h |
| `hypetrain.bar` | `{hypetrain.bar}` | ██████░░░░ 60% | same + meter | L2h |
| `hypetrain.remaining` | `{hypetrain.remaining}` | 1,200 | goal − progress | L2h |
| `hypetrain.timeLeft` | `{hypetrain.timeLeft}` | 4m 10s | `GetHypeTrainStatusAsync` (`expires_at`) | L2h |
| `hypetrain.total` | `{hypetrain.total}` | 5,400 | `GetHypeTrainStatusAsync` | L2h |
| `hypetrain.topContributor` | `{hypetrain.topContributor}` | ViewerOne | `GetHypeTrainStatusAsync` (`top_contributions`) | L2h |
| `hypetrain.record.level` | `{hypetrain.record.level}` | 5 | `GetHypeTrainStatusAsync` (all-time high) | L2h |
| `hypetrain.isGolden` | `{if:{hypetrain.isGolden}\|golden kappa!\|}` | (empty) | `GetHypeTrainStatusAsync` (`type`) | L2h |
| `hypetrain.isShared` | `{if:{hypetrain.isShared}\|shared train\|}` | (empty) | `GetHypeTrainStatusAsync` (`is_shared_train`) | L2h |
| `goal.followers.current` | `{goal.followers.current}` | 1,234 | `ITwitchGoalsApi.GetCreatorGoalsAsync` (type follower) | L2h |
| `goal.followers.target` | `{goal.followers.target}` | 1,500 | `GetCreatorGoalsAsync` | L2h |
| `goal.followers.percent` | `{goal.followers.percent}` | 82% | `GetCreatorGoalsAsync` | L2h |
| `goal.followers.remaining` | `{goal.followers.remaining}` | 266 | `GetCreatorGoalsAsync` | L2h |
| `goal.followers.bar` | `{goal.followers.bar}` | ████████░░ 82% | same + meter | L2h |
| `goal.followers.description` | `{goal.followers.description}` | Road to 1.5k | `GetCreatorGoalsAsync` | L2h |
| `goal.subs.current` | `{goal.subs.current}` | 80 | `GetCreatorGoalsAsync` (type subscription) | L2h |
| `goal.subs.target` | `{goal.subs.target}` | 100 | `GetCreatorGoalsAsync` | L2h |
| `goal.subs.percent` | `{goal.subs.percent}` | 80% | `GetCreatorGoalsAsync` | L2h |
| `goal.subs.remaining` | `{goal.subs.remaining}` | 20 | `GetCreatorGoalsAsync` | L2h |
| `goal.subs.bar` | `{goal.subs.bar}` | ████████░░ 80% | same + meter | L2h |
| `goal.newSubs.current` | `{goal.newSubs.current}` | 6 | `GetCreatorGoalsAsync` (type new_subscription) | L2h |
| `goal.newSubs.percent` | `{goal.newSubs.percent}` | 30% | `GetCreatorGoalsAsync` | L2h |
| `goal.newFollowers.current` | `{goal.newFollowers.current}` | 9 | `GetCreatorGoalsAsync` (type new_follower) | L2h |
| `goal.newFollowers.percent` | `{goal.newFollowers.percent}` | 45% | `GetCreatorGoalsAsync` | L2h |
| `goal.list` | `{goal.list}` | followers 82%, subs 80% | `GetCreatorGoalsAsync` | L2h |
| `goal.bot:<name>.current` | `{goal.bot:hugs.current}` | 40 | bot widget goals (`Infrastructure/Widgets/GoalSeedProvider.cs`) | L2h |
| `goal.bot:<name>.percent` | `{goal.bot:hugs.percent}` | 40% | `GoalSeedProvider.cs` | L2h |
| `charity.name` | `{charity.name}` | Sample Charity | `ITwitchCharityApi.GetCharityCampaignAsync` | L2h |
| `charity.raised` | `{charity.raised}` | €420.00 | `GetCharityCampaignAsync` (`current_amount`) | L2h |
| `charity.target` | `{charity.target}` | €1,000.00 | `GetCharityCampaignAsync` | L2h |
| `charity.percent` | `{charity.percent}` | 42% | `GetCharityCampaignAsync` | L2h |
| `charity.bar` | `{charity.bar}` | ████░░░░░░ 42% | same + meter | L2h |
| `charity.link` | `{charity.link}` | https://www.samplecharity.org | `GetCharityCampaignAsync` (`charity_website`) | L2h |
| `charity.donations.count` | `{charity.donations.count}` | 17 | `ITwitchCharityApi.GetCharityCampaignDonationsAsync` | L2h |
| `charity.donations.latest` | `{charity.donations.latest}` | ViewerOne (€10.00) | `GetCharityCampaignDonationsAsync` | L2h |
| `giveaway.isOpen` | `{if:{giveaway.isOpen}\|type !join\|no giveaway}` | type !join | `IGiveawayService` (`Application/Giveaways/Services/`) | L2h |
| `giveaway.title` | `{giveaway.title}` | Game key | `IGiveawayService` (`GiveawayDto`; field: not checked) | L2h |
| `giveaway.entries` | `{giveaway.entries}` | 37 | `IGiveawayService` | L2h |
| `giveaway.lastWinner` | `{giveaway.lastWinner}` | ViewerTwo | `IGiveawayService` | L2h |
| `giveaway.keyword` | `{giveaway.keyword}` | !join | `IGiveawayService` | L2h |

### 7.11 `ad`, `schedule` — ads and schedule (group 12, lane L2i)

Sources: `ITwitchAdsApi.GetAdScheduleAsync` (`next_ad_at`, `last_ad_at`, `duration`, `preroll_free_time`,
`snooze_count`, `snooze_refresh_at`), already polled by `Infrastructure/Stream/AdBreak/AdScheduleWarner.cs`;
`ITwitchScheduleApi.GetScheduleAsync` (segments, vacation) with `ScheduleTimezone.cs`. Freshness: Cached(30 s) for
ads, Cached(10 min) for the schedule.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `ad.next.in` | `{ad.next.in}` | 12m | `GetAdScheduleAsync` (`next_ad_at`) | L2i |
| `ad.next.in.long` | `{ad.next.in.long}` | 12 minutes | same + Humanizer | L2i |
| `ad.next.at` | `{ad.next.at}` | 15:42 | same + zone | L2i |
| `ad.next.seconds` | `{ad.next.seconds}` | 720 | same | L2i |
| `ad.next.duration` | `{ad.next.duration}` | 1m 30s | `GetAdScheduleAsync` (`duration`) | L2i |
| `ad.last.ago` | `{ad.last.ago}` | 48m | `GetAdScheduleAsync` (`last_ad_at`) | L2i |
| `ad.last.at` | `{ad.last.at}` | 14:42 | same + zone | L2i |
| `ad.snoozes.left` | `{ad.snoozes.left}` | 2 | `GetAdScheduleAsync` (`snooze_count`) | L2i |
| `ad.snoozes.refreshIn` | `{ad.snoozes.refreshIn}` | 34m | `snooze_refresh_at` | L2i |
| `ad.prerollFree` | `{ad.prerollFree}` | 20m | `preroll_free_time` | L2i |
| `ad.isScheduled` | `{if:{ad.isScheduled}\|ad in {ad.next.in}\|no ad planned}` | ad in 12m | `next_ad_at` present | L2i |
| `ad.warning` | `{ad.warning}` | ad break in 12m, grab a snack | **CUT** (a ready-made sentence is the event response's text, not a value) | L2i |
| `schedule.next.at` | `{schedule.next.at}` | Friday 20:00 | `ITwitchScheduleApi.GetScheduleAsync` (first future segment) + zone | L2i |
| `schedule.next.date` | `{schedule.next.date}` | Friday 30 October | same | L2i |
| `schedule.next.in` | `{schedule.next.in}` | 4 days | same + Humanizer | L2i |
| `schedule.next.relative` | `{schedule.next.relative}` | in 4 days | same | L2i |
| `schedule.next.full` | `{schedule.next.full}` | next stream in 4 days, Friday 20:00 | same (Stoney's group 12 form) | L2i |
| `schedule.next.title` | `{schedule.next.title}` | Bot night | segment `title` | L2i |
| `schedule.next.game` | `{schedule.next.game}` | Software and Game Development | segment `category` | L2i |
| `schedule.next.duration` | `{schedule.next.duration}` | 3h | segment `end_time - start_time` | L2i |
| `schedule.next.isRecurring` | `{if:{schedule.next.isRecurring}\|weekly\|one-off}` | weekly | segment `is_recurring` | L2i |
| `schedule.next.viewerTime:<zone>` | `{schedule.next.viewerTime:America/New_York}` | Fri 15:00 | segment + zone conversion (NodaTime) | L2i |
| `schedule.list:<n>` | `{schedule.list:3}` | Fri 20:00 Bot night · Sun 14:00 Chill · Tue 20:00 Bot night | `GetScheduleAsync` | L2i |
| `schedule.week` | `{schedule.week}` | Fri, Sun | `GetScheduleAsync` (this week) | L2i |
| `schedule.today` | `{schedule.today}` | (empty: nothing today) | `GetScheduleAsync` (local day) | L2i |
| `schedule.vacation` | `{schedule.vacation}` | (empty: no break) | `GetScheduleAsync` (`vacation`) | L2i |
| `schedule.vacation.until` | `{schedule.vacation.until}` | (empty) | `vacation.end_time` | L2i |
| `schedule.link` | `{schedule.link}` | https://twitch.tv/samplechannel/schedule | pure (`ChannelName`) | L2i |
| `schedule.ical` | `{schedule.ical}` | https://api.twitch.tv/helix/schedule/icalendar?broadcaster_id=123456 | `ITwitchScheduleApi.GetICalendarAsync` (URL form) | L2i |

### 7.12 `activity` — chat activity (group 13, lane L2i)

Sources: `ChannelContext.MessageCount`, `.SessionChatters`, `.LastActivityAt`; `IChatSearchService`
(`Application/Chat/Services/`); `ChatMetric.cs`, `TopChattersMetric.cs` (`Infrastructure/Dashboard/Stats/`);
`IChannelAnalyticsService.GetChatHoursAsync` → `ChannelChatHoursDto(TimeZone, Cells)`; `ITwitchChatAssetsApi.GetChattersAsync`.
Messages-per-minute needs a rolling window; L2i adds a small in-memory ring per channel beside `ChannelContext`
(not a database read on each render).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `activity.mpm` | `{activity.mpm}` | 18 | rolling 1-minute window (new ring, fed by `ChatMessageHandler.cs`) | L2i |
| `activity.mpm:<minutes>` | `{activity.mpm:5}` | 16 | same, 5-minute average | L2i |
| `activity.messages.stream` | `{activity.messages.stream}` | 2,430 | `ChatMetric.cs` | L2i |
| `activity.messages.today` | `{activity.messages.today}` | 2,610 | `IChatSearchService` (local day) | L2i |
| `activity.messages.hour` | `{activity.messages.hour}` | 1,080 | ring / `IChatSearchService` | L2i |
| `activity.chatters.unique` | `{activity.chatters.unique}` | 33 | `ChannelContext.SessionChatters.Count` | L2i |
| `activity.chatters.now` | `{activity.chatters.now}` | 51 | `ITwitchChatAssetsApi.GetChattersAsync` (total) | L2i |
| `activity.chatters.active:<minutes>` | `{activity.chatters.active:10}` | 12 | ring (distinct authors in window) | L2i |
| `activity.chatters.new` | `{activity.chatters.new}` | 4 | first-time chatters this stream (`ChannelContext.FirstChatChecked` + engagement events) | L2i |
| `activity.chatters.lurkers` | `{activity.chatters.lurkers}` | 18 | `GetChattersAsync` total − unique chatters | L2i |
| `activity.top.today` | `{activity.top.today}` | ViewerOne | `IChatSearchService` (local day) | L2i |
| `activity.top.today.count` | `{activity.top.today.count}` | 140 | same | L2i |
| `activity.top.stream` | `{activity.top.stream}` | ViewerOne (140) | `TopChattersMetric.cs` | L2i |
| `activity.top.list:<n>` | `{activity.top.list:3}` | ViewerOne 140, ViewerTwo 96, ViewerThree 40 | `TopChattersMetric.cs` | L2i |
| `activity.top.hour` | `{activity.top.hour}` | ViewerTwo | ring | L2i |
| `activity.random.weighted` | `{activity.random.weighted}` | ViewerOne (more messages, more likely) | `SessionChatters` + per-author counts this stream | L2i |
| `activity.random.recent:<minutes>` | `{activity.random.recent:5}` | ViewerTwo | ring | L2i |
| `activity.last.chatter` | `{activity.last.chatter}` | ViewerTwo | ring (last author before the trigger) | L2i |
| `activity.last.message.ago` | `{activity.last.message.ago}` | 4s | `ChannelContext.LastActivityAt` | L2i |
| `activity.busiestHour` | `{activity.busiestHour}` | 21:00 | `GetChatHoursAsync` | L2i |
| `activity.busiestDay` | `{activity.busiestDay}` | Friday | `GetChatHoursAsync` | L2i |
| `activity.commands.stream` | `{activity.commands.stream}` | 88 | `ChannelContext.CommandsUsed` | L2i |
| `activity.emotes.top.stream` | `{activity.emotes.top.stream}` | KEKW | chat messages this stream + `ChatEmoteCatalogue.cs` (count) | L2i |
| `activity.firstChatter.stream` | `{activity.firstChatter.stream}` | ViewerTwo | first message after `WentLiveAt` (`IChatSearchService`) | L2i |

### 7.13 `emote` — emotes (group 14, lane L2j)

Sources: `ITwitchChatAssetsApi.GetChannelEmotesAsync`, `GetGlobalEmotesAsync`, `GetUserEmotesAsync`;
`IThirdPartyEmoteProviderRegistry` with `SevenTvEmoteProvider.cs`, `BttvEmoteProvider.cs`, `FfzEmoteProvider.cs`
(`Infrastructure/Chat/Providers/`); `ChatEmoteCatalogue.cs` and `Jobs/ChatTwitchEmoteCacheWarmer.cs` (cache);
`IFavoriteEmoteService`; chat messages for usage counts. Freshness: Cached(10 min) (the warmer keeps it hot).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `emote.random` | `{emote.random}` | sampleHype | `GetChannelEmotesAsync` | L2j |
| `emote.random.sub` | `{emote.random.sub}` | sampleLove | `GetChannelEmotesAsync` (`emote_type` subscriptions) | L2j |
| `emote.random.follower` | `{emote.random.follower}` | sampleWave | `GetChannelEmotesAsync` (follower) | L2j |
| `emote.random.bits` | `{emote.random.bits}` | sampleBits | `GetChannelEmotesAsync` (bitstier) | L2j |
| `emote.random.global` | `{emote.random.global}` | Kappa | `GetGlobalEmotesAsync` | L2j |
| `emote.random.7tv` | `{emote.random.7tv}` | catJAM | `SevenTvEmoteProvider.cs` | L2j |
| `emote.random.bttv` | `{emote.random.bttv}` | catJAM | `BttvEmoteProvider.cs` | L2j |
| `emote.random.ffz` | `{emote.random.ffz}` | monkaS | `FfzEmoteProvider.cs` | L2j |
| `emote.random.any` | `{emote.random.any}` | one emote of all providers | `ChatEmoteCatalogue.cs` | L2j |
| `emote.random.user` | `{emote.random.user}` | an emote the user can type | `GetUserEmotesAsync` (needs the user's token: the bot has it only for the operator; **user side not checked**) | L2j |
| `emote:<name>` | `{emote:catJAM}` | catJAM (only when it exists in the channel; else empty) | `ChatEmoteCatalogue.cs` | L2j |
| `emote:<name>\|<fallback>` | `{emote:catJAM\|:)}` | catJAM | `ChatEmoteCatalogue.cs` | L2j |
| `emote.repeat:<name>\|<n>` | `{emote.repeat:catJAM\|3}` | catJAM catJAM catJAM | `ChatEmoteCatalogue.cs` + pure | L2j |
| `emote.wall:<name>\|<rows>\|<cols>` | `{emote.wall:catJAM\|2\|2}` | catJAM catJAM / catJAM catJAM | same | L2j |
| `emote.list.channel` | `{emote.list.channel}` | sampleHype sampleLove sampleWave | `GetChannelEmotesAsync` | L2j |
| `emote.list.7tv` | `{emote.list.7tv}` | catJAM KEKW … | `SevenTvEmoteProvider.cs` | L2j |
| `emote.list.bttv` | `{emote.list.bttv}` | catJAM … | `BttvEmoteProvider.cs` | L2j |
| `emote.list.ffz` | `{emote.list.ffz}` | monkaS … | `FfzEmoteProvider.cs` | L2j |
| `emote.count.channel` | `{emote.count.channel}` | 12 | `GetChannelEmotesAsync` | L2j |
| `emote.count.7tv` | `{emote.count.7tv}` | 48 | `SevenTvEmoteProvider.cs` | L2j |
| `emote.count.bttv` | `{emote.count.bttv}` | 20 | `BttvEmoteProvider.cs` | L2j |
| `emote.count.ffz` | `{emote.count.ffz}` | 9 | `FfzEmoteProvider.cs` | L2j |
| `emote.user.top` | `{emote.user.top}` | KEKW | the user's chat messages + `ChatEmoteCatalogue.cs` (count) | L2j |
| `emote.user.top.count` | `{emote.user.top.count}` | 212 | same | L2j |
| `emote.user.favorite` | `{emote.user.favorite}` | catJAM | `IFavoriteEmoteService.ListAsync` (operator favorites; per-viewer: not checked) | L2j |
| `emote.channel.top` | `{emote.channel.top}` | KEKW | channel chat messages + catalogue (all time, Cached(1 h)) | L2j |
| `emote.channel.top.stream` | `{emote.channel.top.stream}` | catJAM | same, this stream | L2j |
| `emote.message.count` | `{emote.message.count}` | 3 | the trigger message + catalogue | L2j |
| `emote.message.first` | `{emote.message.first}` | KEKW | same | L2j |
| `emote.image:<name>` | `{emote.image:catJAM}` | https://cdn.7tv.app/emote/…/2x.webp | `ChatEmoteCatalogue.cs` (URL) | L2j |
| `emote.provider:<name>` | `{emote.provider:catJAM}` | 7TV | `ChatEmoteCatalogue.cs` | L2j |
| `emote.newest.7tv` | `{emote.newest.7tv}` | PepeLaugh | `SevenTvEmoteProvider.cs` (added date: not checked) | L2j |
| `emote.cheermote:<n>` | `{emote.cheermote:100}` | Cheer100 | `ITwitchBitsApi.GetCheermotesAsync` | L2j |

### 7.14 `chat`, `tts`, `link` — chat and TTS formatting (group 9, lane L2j)

Pure in `Helpers/Chat/` unless named. The 500-character limit is Twitch's chat limit; Kick and YouTube limits are
read from the platform adapter (`IChatProvider`), so `chat.fit` uses the delivering platform's limit.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `chat.fit:<t>` | `{chat.fit:{music.queue.list:50}}` | the list cut at the platform limit with " …" | pure + `IChatProvider` limit | L2j |
| `chat.fit:<t>\|<end>` | `{chat.fit:{text}\|(more on the page)}` | text cut to fit, then "(more on the page)" | pure | L2j |
| `chat.fit.words:<t>` | `{chat.fit.words:{text}}` | cut at a word edge | pure | L2j |
| `chat.limit` | `{chat.limit}` | 500 | `IChatProvider` (delivering platform) | L2j |
| `chat.mention:<name>` | `{chat.mention:ViewerTwo}` | @ViewerTwo | pure (`ChatMentionName.cs` login rule for non-Latin names) | L2j |
| `chat.mention.off:<name>` | `{chat.mention.off:ViewerTwo}` | ViewerTwo (no @, no ping) | pure | L2j |
| `chat.mention.soft:<name>` | `{chat.mention.soft:ViewerTwo}` | V​iewerTwo (zero-width space: no ping) | pure | L2j |
| `chat.mention.user` | `{chat.mention.user}` | @ViewerOne | trigger seed + `ChatMentionName` | L2j |
| `chat.mention.reply` | `{chat.mention.reply}` | (empty on a threaded reply, @ViewerOne otherwise) | send mode seed (`SendReplyAction.cs`) | L2j |
| `chat.me:<t>` | `{chat.me:dances}` | /me dances (Twitch action line; plain text where unsupported) | `IChatProvider` | L2j |
| `chat.message` | `{chat.message}` | hello big world (the trigger message) | trigger seed | L2j |
| `chat.message.noCommand` | `{chat.message.noCommand}` | hello big world | the `message` seed (the chat line, seeded by `ChatMessageHandler.BuildInitialVariables`) minus its leading command word (a first word that starts with the channel's command prefix); a message without a command word stays whole | L2j |
| `chat.message.id` | `{chat.message.id}` | msg-1 | trigger seed | L2j |
| `chat.message.length` | `{chat.message.length}` | 15 | trigger seed | L2j |
| `chat.message.isAction` | `{if:{chat.message.isAction}\|/me\|}` | (empty) | trigger seed | L2j |
| `chat.message.bits` | `{chat.message.bits}` | 0 | trigger seed (`ChatMessage.BitsAmount`) | L2j |
| `chat.message.isFirst` | `{if:{chat.message.isFirst}\|first message!\|}` | (empty) | first-chat flag | L2j |
| `chat.isShared` | `{if:{chat.isShared}\|shared chat\|}` | (empty) | `ISharedChatSessionTracker` (`Infrastructure/Chat/`) | L2j |
| `chat.shared.channels` | `{chat.shared.channels}` | OtherStreamer | `ITwitchChatAssetsApi.GetSharedChatSessionAsync` | L2j |
| `chat.settings.slow` | `{chat.settings.slow}` | 3s | `ITwitchChatApi.GetChatSettingsAsync` | L2j |
| `chat.settings.followersOnly` | `{chat.settings.followersOnly}` | 10m | `GetChatSettingsAsync` | L2j |
| `chat.settings.subOnly` | `{if:{chat.settings.subOnly}\|sub-only\|open}` | open | `GetChatSettingsAsync` | L2j |
| `chat.settings.emoteOnly` | `{if:{chat.settings.emoteOnly}\|emote-only\|}` | (empty) | `GetChatSettingsAsync` | L2j |
| `chat.pinned` | `{chat.pinned}` | Welcome to the stream | `ITwitchChatApi.GetPinnedMessagesAsync` | L2j |
| `tts.plain:<t>` | `{tts.plain:hi @ViewerTwo KEKW https://a.b 𝐡𝐢}` | hi ViewerTwo hi | pure + `ChatEmoteCatalogue.cs` (drops emotes, links, @, folds fancy letters) | L2j |
| `tts.plain.keepEmotes:<t>` | `{tts.plain.keepEmotes:hi KEKW}` | hi KEKW | pure | L2j |
| `tts.numbers:<t>` | `{tts.numbers:I have 3 cats}` | I have three cats | Humanizer `ToWords` | L2j |
| `tts.lexicon:<t>` | `{tts.lexicon:gg}` | good game | `ITtsLexiconService` (channel pronunciation list) | L2j |
| `tts.voice` | — | **CUT** (section 12: no helper picks a voice) | — | L2j |
| `tts.queue.length` | `{tts.queue.length}` | 2 | `ITtsDispatchService` / `ITtsHistoryService` (queue read: not checked) | L2j |
| `tts.audioConnected` | `{if:{tts.audioConnected}\|TTS is on\|TTS page closed}` | TTS is on | as-built `ResolveAudioSourceConnected` | L2j |
| `link.x:<t>` | `{link.x:Watch {channel.url}}` | https://x.com/intent/post?text=Watch%20https%3A%2F%2Ftwitch.tv%2Fsamplechannel | pure (click-to-post, as Nightbot) | L2j |
| `link.bluesky:<t>` | `{link.bluesky:Live now}` | https://bsky.app/intent/compose?text=Live%20now | pure | L2j |
| `link.threads:<t>` | `{link.threads:Live now}` | https://www.threads.net/intent/post?text=Live%20now | pure | L2j |
| `link.mastodon:<instance>\|<t>` | `{link.mastodon:mastodon.social\|Live now}` | https://mastodon.social/share?text=Live%20now | pure | L2j |
| `link.short:<url>` | — | **CUT** (needs an outside shortener with an account) | — | L2j |

### 7.15 `platform` — multi-platform (group 10, lane L2k)

Sources: trigger seed `user.provider`; `ChannelContext.ViewerCount` (Twitch); `IKickApiClient`
(`Application/Contracts/Kick/IKickApiClient.cs`; `KickApiClient.cs` reads `ViewerCount`); YouTube live viewers in
`Infrastructure/Chat/YouTube/YouTubeLiveChatPollWorker.cs` (`ViewerCount`); `IChannelAnalyticsService.GetPlatformSummaryAsync`
(per-platform totals); the channel's platform connections (user identities, `IUserIdentityService`).
Freshness: Cached(60 s) per platform. A platform that is not connected renders nothing (never 0 as if it were).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `platform.user` | `{platform.user}` | twitch | trigger seed | L2k |
| `platform.user.name` | `{platform.user.name}` | Twitch | trigger seed | L2k |
| `platform.user.icon` | `{platform.user.icon}` | 🟣 | trigger seed + fixed map (Twitch 🟣, Kick 🟢, YouTube 🔴) | L2k |
| `platform.event` | `{platform.event}` | twitch | event seed (as-built `provider`) | L2k |
| `platform.viewers.total` | `{platform.viewers.total}` | 60 | Twitch + Kick + YouTube counts | L2k |
| `platform.viewers.twitch` | `{platform.viewers.twitch}` | 42 | `ChannelContext.ViewerCount` | L2k |
| `platform.viewers.kick` | `{platform.viewers.kick}` | 10 | `IKickApiClient` (`KickApiClient.cs`) | L2k |
| `platform.viewers.youtube` | `{platform.viewers.youtube}` | 8 | `YouTubeLiveChatPollWorker.cs` | L2k |
| `platform.viewers.breakdown` | `{platform.viewers.breakdown}` | Twitch 42 · Kick 10 · YouTube 8 | the three above | L2k |
| `platform.viewers.share:<platform>` | `{platform.viewers.share:kick}` | 17% | same | L2k |
| `platform.live.list` | `{platform.live.list}` | Twitch, Kick, YouTube | live state per connection | L2k |
| `platform.live.count` | `{platform.live.count}` | 3 | same | L2k |
| `platform.isLive:<platform>` | `{if:{platform.isLive:youtube}\|on YouTube too\|}` | on YouTube too | YouTube live session (`IYouTubeLiveChatSessionRegistry`) | L2k |
| `platform.link.twitch` | `{platform.link.twitch}` | https://twitch.tv/samplechannel | channel connection (pure) | L2k |
| `platform.link.kick` | `{platform.link.kick}` | https://kick.com/samplechannel | Kick connection (`IKickApiClient` slug) | L2k |
| `platform.link.youtube` | `{platform.link.youtube}` | https://youtube.com/@samplechannel | YouTube connection (`IYouTubeOEmbedReader`/identity; handle field: not checked) | L2k |
| `platform.link.youtube.live` | `{platform.link.youtube.live}` | https://youtube.com/watch?v=sample | `IYouTubeLiveChatSessionRegistry` (video id) | L2k |
| `platform.links` | `{platform.links}` | twitch.tv/samplechannel · kick.com/samplechannel · youtube.com/@samplechannel | all connections | L2k |
| `platform.followers.twitch` | `{platform.followers.twitch}` | 1,234 | `GetChannelFollowerCountAsync` | L2k |
| `platform.followers.kick` | `{platform.followers.kick}` | 210 | `IKickApiClient` (followers field: not checked) | L2k |
| `platform.subscribers.youtube` | `{platform.subscribers.youtube}` | 3,400 | YouTube Data API channel statistics (client: not checked) | L2k |
| `platform.followers.total` | `{platform.followers.total}` | 4,844 | the three above | L2k |
| `platform.messages.stream:<platform>` | `{platform.messages.stream:kick}` | 120 | chat messages by provider this stream | L2k |
| `platform.chatters.stream:<platform>` | `{platform.chatters.stream:youtube}` | 6 | same, distinct authors | L2k |
| `platform.top` | `{platform.top}` | Twitch | `GetPlatformSummaryAsync` (most viewers) | L2k |
| `platform.connected` | `{platform.connected}` | Twitch, Kick, YouTube | channel connections | L2k |
| `platform.count` | `{platform.count}` | 3 | same | L2k |
| `platform.title:<platform>` | `{platform.title:youtube}` | Building a bot | YouTube live broadcast title (session registry; field: not checked) | L2k |
| `platform.uptime:<platform>` | `{platform.uptime:kick}` | 2h 14m | Kick livestream `created_at` (`IKickApiClient`; field: not checked) | L2k |

### 7.16 `args` — command arguments (group 15, lane L2k)

Source: the command seed (`CommandInvoker.cs` builds `args`, `args.<n>`). The `args.<n>.user.*` rows are in 7.7.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `args.count` | `{args.count}` | 4 | command seed | L2k |
| `args.rest` | `{args.rest}` | hello big world (all after the first) | command seed | L2k |
| `args.<n>+` | `{args.2+}` | hello big world | command seed | L2k |
| `args.<a>-<b>` | `{args.2-3}` | hello big | command seed | L2k |
| `args.last` | `{args.last}` | world | command seed | L2k |
| `args.first` | `{args.first}` | @ViewerTwo | command seed | L2k |
| `args.<n>.clean` | `{args.1.clean}` | ViewerTwo (no @) | command seed | L2k |
| `args.<n>.number` | `{args.3.number}` | (empty: not a number) | command seed + pure parse | L2k |
| `args.<n>.number:<default>` | `{args.3.number:1}` | 1 | same | L2k |
| `args.<n>.isNumber` | `{if:{args.2.isNumber}\|n\|word}` | word | same | L2k |
| `args.<n>.isMention` | `{if:{args.1.isMention}\|mention\|text}` | mention | same | L2k |
| `args.<n>:<default>` | `{args.5:nobody}` | nobody | same | L2k |
| `args.<n>.upper` | `{args.2.upper}` | HELLO | same + text | L2k |
| `args.<n>.length` | `{args.2.length}` | 5 | same | L2k |
| `args.all.quoted` | `{args.all.quoted}` | "@ViewerTwo hello big world" | same | L2k |
| `args.all.urlEncoded` | `{args.all.urlEncoded}` | %40ViewerTwo%20hello%20big%20world | same (as Streamer.bot `rawInputUrlEncoded`) | L2k |
| `args.<n>.urlEncoded` | `{args.2.urlEncoded}` | hello | same | L2k |
| `args.isEmpty` | `{if:{args.isEmpty}\|usage: !cmd <name>\|ok}` | ok | same | L2k |
| `args.mentions` | `{args.mentions}` | @ViewerTwo | same (all @ words) | L2k |
| `args.mentions.count` | `{args.mentions.count}` | 1 | same | L2k |
| `args.numbers` | `{args.numbers}` | (empty) | same (all numeric words) | L2k |
| `args.duration:<n>` | `{args.duration:2}` (`!timer 5m`) | 5m → 300 | same + pure duration parse | L2k |
| `args.match:<n>` | `{args.match:1}` | the n-th regex capture of a regex-trigger command | trigger seed (regex triggers, `IChatTriggerService`; capture seeding: not checked) | L2k |
| `command.name` | `{command.name}` | cmd | command seed | L2k |
| `command.prefix` | `{command.prefix}` | ! | `ChannelContext.CommandPrefix` | L2k |
| `command.trigger` | `{command.trigger}` | !cmd | command seed | L2k |
| `command.alias` | `{command.alias}` | c (the alias used, else the name) | command seed | L2k |
| `command.uses` | `{command.uses}` | 31 | `IChannelAnalyticsService.GetTopCommandsAsync` (`Uses`) | L2k |
| `command.uses.user` | `{command.uses.user}` | 4 | command runs by this user (runs table; query: not checked) | L2k |
| `command.cooldown` | `{command.cooldown}` | 30s | `ChannelContext.Commands` (`CachedCommand`) | L2k |
| `command.cooldown.left` | `{command.cooldown.left}` | 12s | `ChannelContext.Cooldowns` | L2k |
| `command.level` | `{command.level}` | everyone | `CachedCommand` (permission, plain word; never a number) | L2k |

### 7.17 `bot` — bot and system (group 18, lane L2l)

Sources: `TemplateResolver.GetBotNameAsync`; `AssemblyInformationalVersionAttribute` (`NomNomzBot.Api/Program.cs`
reads it); process start (`Environment.TickCount64` / `Process.StartTime`); `ChannelContext.Commands`,
`.LoadedAt`; `IBuiltinCommandService`, `ICommandService`, `ITimerManagementService`; the public command page route
(not checked; L2l checks the dashboard router for the viewer command list path).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `bot.name` | `{bot.name}` | SampleBot | `GetBotNameAsync` | L2l |
| `bot.mention` | `{bot.mention}` | @SampleBot | same | L2l |
| `bot.version` | `{bot.version}` | 1.42.0 | `AssemblyInformationalVersionAttribute` | L2l |
| `bot.uptime` | `{bot.uptime}` | 3d 4h | process start | L2l |
| `bot.uptime.long` | `{bot.uptime.long}` | 3 days, 4 hours | same + Humanizer | L2l |
| `bot.startedAt` | `{bot.startedAt}` | 2026-10-23 10:30 | process start + zone | L2l |
| `bot.channel.loadedAgo` | `{bot.channel.loadedAgo}` | 2h 20m | `ChannelContext.LoadedAt` | L2l |
| `bot.isMod` | `{if:{bot.isMod}\|I can moderate\|please /mod me}` | I can moderate | `IBotModeratorStatusReader` | L2l |
| `bot.prefix` | `{bot.prefix}` | ! | `ChannelContext.CommandPrefix` | L2l |
| `bot.commands.list` | `{bot.commands.list}` | !hug !lurk !so !uptime … | `ChannelContext.Commands` + enabled builtins (`DisabledBuiltins`) | L2l |
| `bot.commands.list.custom` | `{bot.commands.list.custom}` | !hug !lurk | `ChannelContext.Commands` | L2l |
| `bot.commands.list.builtin` | `{bot.commands.list.builtin}` | !so !uptime !sr | `IBuiltinCommandService` minus `DisabledBuiltins` | L2l |
| `bot.commands.list.forUser` | `{bot.commands.list.forUser}` | !hug !lurk !sr (what this user may run) | same + permission check | L2l |
| `bot.commands.count` | `{bot.commands.count}` | 24 | same | L2l |
| `bot.commands.link` | `{bot.commands.link}` | https://nomnomz.bot/c/samplechannel/commands | public viewer page (route: not checked) | L2l |
| `bot.commands.fit` | `{bot.commands.fit}` | the list cut to the chat limit, then the link | `chat.fit` + the two above | L2l |
| `bot.timers.count` | `{bot.timers.count}` | 5 | `ITimerManagementService` | L2l |
| `bot.timers.next` | `{bot.timers.next}` | !discord in 4m | `ITimerManagementService` (next run: not checked) | L2l |
| `bot.personality` | `{bot.personality}` | Informative | `ChannelContext.Personality` | L2l |
| `bot.language` | `{bot.language}` | en | `ChannelContext.Language` | L2l |
| `bot.dashboard` | `{bot.dashboard}` | https://nomnomz.bot/app/u/samplechannel | public origin (memory rule: public origin, not request scheme) | L2l |
| `bot.sourceCode` | `{bot.sourceCode}` | https://github.com/NoMercyLabs/nomnomzbot | fixed (public source link) | L2l |
| `bot.license` | `{bot.license}` | PolyForm-Noncommercial-1.0.0 | fixed | L2l |
| `bot.ping` | `{bot.ping}` | 42 ms | last EventSub/chat round-trip (source: not checked; L2l checks the WebSocket keepalive timing) | L2l |
| `bot.lastRestart.relative` | `{bot.lastRestart.relative}` | 3 days ago | process start | L2l |

### 7.18 `fun` — fun (group 17, lane L2l)

Answer lists (8-ball, coin words) are **channel data with neutral defaults per personality tone**
(`ChannelContext.Personality`; memory rule parity-values-are-channel-data), stored as pick lists the streamer can
edit (`IPickListService`), seeded once per channel. Random rows run seeded in tests.

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `fun.8ball` | `{fun.8ball}` | one answer of the channel's 8-ball list in its tone | `IPickListService` list `8ball` + `ChannelContext.Personality` default seed | L2l |
| `fun.8ball.yes` | `{fun.8ball.yes}` | a positive answer | same list, positive part | L2l |
| `fun.8ball.no` | `{fun.8ball.no}` | a negative answer | same | L2l |
| `fun.coin` | `{fun.coin}` | heads or tails | pure (seeded) + channel words | L2l |
| `fun.coin.emoji` | `{fun.coin.emoji}` | 🪙 heads | pure | L2l |
| `fun.dice` | `{fun.dice}` | 1..6 | pure | L2l |
| `fun.dice:<sides>` | `{fun.dice:20}` | 1..20 | pure | L2l |
| `fun.dice:<n>d<sides>` | `{fun.dice:2d6}` | 2..12 | pure | L2l |
| `fun.dice.detail:<n>d<sides>` | `{fun.dice.detail:2d6}` | 3 + 5 = 8 | pure | L2l |
| `fun.dice.emoji` | `{fun.dice.emoji}` | ⚀..⚅ | pure | L2l |
| `fun.dice.emoji:<n>` | `{fun.dice.emoji:3}` | three die faces | pure | L2l |
| `fun.rps` | `{fun.rps}` | 🪨 rock, 📄 paper or ✂️ scissors | pure | L2l |
| `fun.rps.vs:<choice>` | `{fun.rps.vs:rock}` | paper — you lose | pure | L2l |
| `fun.card` | `{fun.card}` | Q♥ | pure (52-card deck) | L2l |
| `fun.slots` | `{fun.slots}` | 🍒 🍋 🍒 | pure | L2l |
| `fun.slots.win` | `{if:{fun.slots.win}\|jackpot\|try again}` | try again (reads the last `fun.slots` in this render) | render scope | L2l |
| `fun.love:<a>\|<b>` | `{fun.love:{user.display}\|{target.display}}` | 73% | pure, stable per pair and day (hash) | L2l |
| `fun.rate:<thing>` | `{fun.rate:pizza}` | 8/10 | pure, stable per thing and day | L2l |
| `fun.percent:<thing>` | `{fun.percent:luck}` | 64% | pure, stable per user and day | L2l |
| `fun.size` | `{fun.size}` | 42 cm | **CUT** (bully-data risk; memory rule viewer-pages-no-bully-data) | L2l |
| `fun.meter:<value>` | `{meter:{random.number:100}}` | ██████░░░░ 60% | pure (Stoney's group 17 form, = `meter`) | L2l |
| `fun.fact` | `{fun.fact}` | a random fact | `lookup` source uselessfacts (section 9) | L2m |
| `fun.fact:<lang>` | `{fun.fact:de}` | a German fact | same (`en`, `de` only) | L2m |
| `fun.dadjoke` | `{fun.dadjoke}` | a dad joke | icanhazdadjoke (section 9) | L2m |
| `fun.dadjoke.search:<term>` | `{fun.dadjoke.search:cat}` | a dad joke about cats | icanhazdadjoke `/search` (not checked) | L2m |
| `fun.random.emoji` | `{fun.random.emoji}` | 🎉 | pure (fixed safe set) | L2l |
| `fun.random.animal` | `{fun.random.animal}` | 🦊 fox | pure | L2l |
| `fun.random.food` | `{fun.random.food}` | 🍕 pizza | pure | L2l |
| `fun.random.color.name` | `{fun.random.color.name}` | teal | pure | L2l |
| `fun.countdown:<n>` | `{fun.countdown:3}` | 3… 2… 1… | pure | L2l |
| `fun.roulette` | `{fun.roulette}` | click (or bang 1 in 6) | pure | L2l |
| `fun.hug.count` | `{count.hugs.user:+1}` | alias, see 7.8 | `IViewerDataService` | L2f |
| `fun.ship:<a>\|<b>` | `{fun.ship:ViewerOne\|ViewerTwo}` | ViewerTwo + ViewerOne = ViewTwo | pure (name blend) | L2l |
| `fun.flip:<t>` | `{fun.flip:table}` | (╯°□°)╯︵ ǝlqɐʇ | pure (upside-down map) | L2l |
| `fun.unflip` | `{fun.unflip}` | ┬─┬ノ( º _ ºノ) | pure | L2l |
| `fun.shrug` | `{fun.shrug}` | ¯\\\_(ツ)\_/¯ | pure | L2l |
| `fun.lenny` | `{fun.lenny}` | ( ͡° ͜ʖ ͡°) | pure | L2l |

### 7.19 `lookup` — outside lookups (group 7, lane L2m)

Every source is in section 9 with URL, limits, cache and fallback. A lookup never blocks chat longer than its
timeout; on failure it renders the row's fallback (channel-language string `template_helper_lookup_*_failed`).
Twitch user lookups use the bot's own Helix client (no outside source).

| Name | Example | Output | Real data source | Lane |
|---|---|---|---|---|
| `lookup.twitch:<login>.game` | `{lookup.twitch:{args.1}.game}` | Minecraft | `ITwitchChannelsApi.GetChannelInformationByTwitchIdsAsync` | L2m |
| `lookup.twitch:<login>.title` | `{lookup.twitch:viewertwo.title}` | Building castles | same | L2m |
| `lookup.twitch:<login>.isLive` | `{if:{lookup.twitch:viewertwo.isLive}\|live\|offline}` | offline | `ITwitchStreamsApi.GetStreamsAsync` | L2m |
| `lookup.twitch:<login>.viewers` | `{lookup.twitch:viewertwo.viewers}` | (empty: offline) | `GetStreamsAsync` | L2m |
| `lookup.twitch:<login>.followers` | `{lookup.twitch:viewertwo.followers}` | 950 | `GetChannelFollowerCountAsync` | L2m |
| `lookup.twitch:<login>.display` | `{lookup.twitch:viewertwo.display}` | ViewerTwo | `ITwitchUsersApi.GetUsersByLoginsAsync` | L2m |
| `lookup.twitch:<login>.avatar` | `{lookup.twitch:viewertwo.avatar}` | https://static-cdn.jtvnw.net/…/viewertwo.png | `GetUsersByLoginsAsync` | L2m |
| `lookup.twitch:<login>.createdAt` | `{lookup.twitch:viewertwo.createdAt}` | 2019-05-02 | `GetUsersByLoginsAsync` | L2m |
| `lookup.twitch:<login>.uptime` | `{lookup.twitch:viewertwo.uptime}` | (empty: offline) | `GetStreamsAsync` (`started_at`) | L2m |
| `lookup.twitch:<login>.lastGame` | `{lookup.twitch:viewertwo.lastGame}` | Minecraft | `GetChannelInformationByTwitchIdsAsync` | L2m |
| `lookup.twitch:<login>.url` | `{lookup.twitch:viewertwo.url}` | https://twitch.tv/viewertwo | pure | L2m |
| `lookup.twitch:<login>.followage:<channel>` | `{lookup.twitch:viewertwo.followage:samplechannel}` | 9 months | `ITwitchChannelsApi.GetChannelFollowerAsync` | L2m |
| `lookup.game:<name>.boxart` | `{lookup.game:Minecraft.boxart}` | https://static-cdn.jtvnw.net/ttv-boxart/27471-285x380.jpg | `ITwitchGamesApi.GetGamesAsync` | L2m |
| `lookup.game.top` | `{lookup.game.top}` | Just Chatting | `ITwitchGamesApi.GetTopGamesAsync` | L2m |
| `lookup.weather:<city>` | `{lookup.weather:Amsterdam}` | Amsterdam: 12 °C, light rain | Open-Meteo geocoding + forecast | L2m |
| `lookup.weather:<city>.temp` | `{lookup.weather:Amsterdam.temp}` | 12 °C | Open-Meteo | L2m |
| `lookup.weather:<city>.temp.f` | `{lookup.weather:Amsterdam.temp.f}` | 54 °F | Open-Meteo (`temperature_unit=fahrenheit`) | L2m |
| `lookup.weather:<city>.feels` | `{lookup.weather:Amsterdam.feels}` | 10 °C | Open-Meteo `apparent_temperature` | L2m |
| `lookup.weather:<city>.condition` | `{lookup.weather:Amsterdam.condition}` | light rain | Open-Meteo `weather_code` (WMO table, channel language) | L2m |
| `lookup.weather:<city>.emoji` | `{lookup.weather:Amsterdam.emoji}` | 🌧️ | `weather_code` | L2m |
| `lookup.weather:<city>.wind` | `{lookup.weather:Amsterdam.wind}` | 18 km/h SW | Open-Meteo `wind_speed_10m`, `wind_direction_10m` | L2m |
| `lookup.weather:<city>.humidity` | `{lookup.weather:Amsterdam.humidity}` | 81% | Open-Meteo `relative_humidity_2m` | L2m |
| `lookup.weather:<city>.tomorrow` | `{lookup.weather:Amsterdam.tomorrow}` | 9–14 °C, showers | Open-Meteo daily | L2m |
| `lookup.time:<city>` | `{lookup.time:Tokyo}` | 23:30 | Open-Meteo geocoding (`timezone`) + NodaTime | L2m |
| `lookup.time:<city>.date` | `{lookup.time:Tokyo.date}` | Monday 26 October | same | L2m |
| `lookup.time:<city>.offset` | `{lookup.time:Tokyo.offset}` | +8h from here | same + channel zone | L2m |
| `lookup.currency:<n>\|<from>\|<to>` | `{lookup.currency:10\|EUR\|USD}` | 10 EUR = 11.62 USD (rate of the fixture) | Frankfurter `/v2/rate/eur/usd` | L2m |
| `lookup.currency.rate:<from>\|<to>` | `{lookup.currency.rate:EUR\|USD}` | 1.162 | Frankfurter | L2m |
| `lookup.currency.date` | `{lookup.currency.date}` | 2026-10-26 | Frankfurter (rate date) | L2m |
| `lookup.define:<word>` | `{lookup.define:bot}` | bot (noun): a program that does a task automatically | Free Dictionary API | L2m |
| `lookup.define:<word>.all` | `{lookup.define:run.all}` | the meanings joined, cut to the chat limit | same | L2m |
| `lookup.define:<word>.phonetic` | `{lookup.define:bot.phonetic}` | /bɒt/ | same | L2m |
| `lookup.define:<word>.synonyms` | `{lookup.define:happy.synonyms}` | glad, joyful | same | L2m |
| `lookup.define:<word>.example` | `{lookup.define:bot.example}` | the first usage example | same | L2m |
| `lookup.translate:<lang>\|<t>` | `{lookup.translate:nl\|good morning}` | goedemorgen (Dutch output) | MyMemory `/get` | L2m |
| `lookup.translate:<from>\|<to>\|<t>` | `{lookup.translate:de\|en\|guten Morgen}` | good morning | MyMemory | L2m |
| `lookup.translate.chat:<lang>` | `{lookup.translate.chat:en}` | the trigger message in English | MyMemory + trigger seed | L2m |
| `lookup.fact` | `{lookup.fact}` | alias of `fun.fact` | uselessfacts | L2m |
| `lookup.dadjoke` | `{lookup.dadjoke}` | alias of `fun.dadjoke` | icanhazdadjoke | L2m |
| `custom.<source>.<field>` | `{custom.weather.temp}` | 21.5 | as-built custom data (`ICustomDataPollService`): the streamer's own URL source, which covers "urlfetch"/"customapi" | L1 |

### 7.20 Cut table — forms that are not built

Each cut has a reason. A cut row that has a real source later moves into the catalog; nothing here is silently
dropped. "Seen in" names the products whose docs list the form (section 10 has the URLs).

| Form | Seen in | Reason for the cut | Where the need is met instead |
|---|---|---|---|
| `stream.marker` | none (Twitch Helix `Create Stream Marker`) | An action that writes to Twitch, not a read. A template must stay free of side effects. | A pipeline action `CreateStreamMarker` (every-action-reachable-everywhere: also in the SDK). |
| `user.shoutoutLine` | none | A shout-out text is the builtin's own reply, not a value. | The shout-out builtin and its template. |
| `ad.warning` | none | A ready-made sentence belongs to the event response's text. | `{ad.nextIn}` and `{if:…}` inside the response. |
| `tts.voice` | none | Rule bot-speaks-in-one-voice: no helper picks a voice. See section 12. | The TTS action keeps the channel's one bot voice. |
| `link.short:<url>` | none | Needs an outside shortener with an account and a key. | None. A streamer pastes a short link. |
| `fun.size` | none | Bully-data risk (rule viewer-pages-no-bully-data). | None. |
| last host / `host.*` | none | Twitch removed hosting. Not checked against the live Helix reference in this lane; L2d confirms before it adds any `host` row. | Raid rows (`event.viewers`). |
| `eval` / inline JavaScript | Nightbot `eval`, Fossabot `eval` | Runs user code inside a chat template: a security surface. | The scripts SDK (section 11) with its sandbox. |
| `stockprice` | StreamElements | Every free stock API needs a key and has a licence limit. Not checked which. | None. |
| League of Legends, TFT, PUBG, Valorant, Steam lookups | Nightbot, StreamElements, Fossabot | Each needs a game-company key and is off the Twitch scope. | `custom.<source>.<field>` (the streamer's own URL source). |
| `lastfm` | Nightbot, Fossabot | Needs a key per streamer. | `{music.*}` rows (Spotify and the song queue). |
| `ai` / chatgpt | StreamElements | An AI model in a template is not an approved source. | None. |
| TikTok / YouTube latest video | Fossabot | No client for it in this repo. | None. |
| `setgame` / `settitle` | StreamElements, Fossabot | Actions, not reads. | Pipeline actions `SetStreamGame`, `SetStreamTitle`. |
| `redeem` | StreamElements | An action. | Pipeline reward actions. |
| random DJ Khaled quote / Rainwave | Nightbot | No real value for a stream; no maintained source. | None. |
| `urlfetch` / `customapi` as a free URL | Nightbot, StreamElements, Fossabot | Fetching any URL typed in a template is a request-forgery risk. | `custom.<source>.<field>`: the streamer registers the URL once, and the poller fetches it (as-built). |

## 8. Lanes

L1 owns every shared file. The other lanes add only inside their own folders, so they run in parallel with no merge
conflict. At most 2 lanes build or test at the same time (machine load cap).

| Lane | Owns (folder `Helpers/<X>/`, strings file, test folder) | Catalog rows | Rows |
|---|---|---|---|
| **L1 core** | `TemplateParser`, `ITemplateHelperProvider`, the registry aggregate, reflection DI, coverage tests, i18n move, `TemplateHelperTypeScript` writer, `date` wrapper, `POST templates/render`, the SDK `render` call. Owns **all** shared files: `TemplateResolver.cs`, `TemplateHelperRegistry.cs`, `TemplateHelperValidator.cs`, `TemplatesController.cs`, `SdkRuntimeSurface.cs`, `DependencyInjection.cs`. | 7.1, 7.2, 7.19 last row, `{{` | 188 |
| L2a | `Helpers/Text/`, `strings_template_text.xml` | 7.3 | 108 |
| L2b | `Helpers/Numbers/` (`num`, `math`, `meter`) | 7.4 | 76 |
| L2c | `Helpers/Logic/` (`if`, `switch`, `default`, `chance`, `pick`, `set`, `var`, `include`, `random`, `roll`, `quote`) | 7.5 | 61 |
| L2d | `Helpers/Stream/` (`stream`, `channel`, `clip`) | 7.6 | 92 |
| L2e | `Helpers/People/` (`user`, `target`, `viewer`) | 7.7 | 94 |
| L2f | `Helpers/Counters/` (`count`, `list`) | 7.8 | 36 |
| L2g | `Helpers/Music/` | 7.9 | 51 |
| L2h | `Helpers/LiveEvents/` (`poll`, `prediction`, `hypetrain`, `goal`, `charity`, `giveaway`) | 7.10 | 85 |
| L2i | `Helpers/Ads/`, `Helpers/Activity/` (`ad`, `schedule`, `activity`) | 7.11, 7.12 | 52 |
| L2j | `Helpers/Emotes/`, `Helpers/Chat/` (`emote`, `chat`, `tts`, `link`) | 7.13, 7.14 | 67 |
| L2k | `Helpers/Platforms/`, `Helpers/Arguments/` (`platform`, `args`, `command`) | 7.15, 7.16 | 61 |
| L2l | `Helpers/Bot/`, `Helpers/Fun/` | 7.17, 7.18 | 56 |
| L2m | `Helpers/Lookups/` + one client per outside source | 7.19 | 43 |

(The Rows column is the count of the lane column in the catalog; the command is in section 13. L1 holds 188 because
it also owns the 14 date rows and 3 L1 rows listed under other sections: `{{`/`}}`, `\{`/`\}` and `custom.<source>.<field>`.)

Rules for every lane brief (checked by `brief-check`):

1. Order: L1 first (parser, provider interface, registry, writer, tests). L2 lanes start when L1's interface is on
   `master`. L2 lanes run in parallel, at most 2 building at a time.
2. A lane touches no shared file. A row that needs a shared change goes to L1 as a request in the lane's handoff.
3. Each lane ships: provider class(es) of one namespace each, `strings_template_<ns>.xml` in `values` and `values-nl`,
   tests that render every row against the section 5 fixture and assert the Output cell (state, not "returned
   non-null"), and a `Sample` on every entry.
4. C# briefs state: explicit types, never `var` (only for anonymous types); the license header on each new file;
   `Result<T>` over exceptions; run `scripts/slice-check.ps1`; names are `NomNomzBot.*`.
5. A helper with a per-streamer word list or value reads it as channel data with a neutral default
   (parity-values-are-channel-data), never a constant.
6. One item per builder: a lane with more than 60 rows splits into sub-slices (for example L2a: case and trim, then
   pad and wrap, then split and join), each its own commit.

## 9. Outside lookups

Fetched or opened on 2026-10-26 in this lane (status code and the first bytes read). Limits are quoted from the
provider's page; "not stated" means the page gave none, and L2m checks again before shipping.

| Source | URL | Key | Limits and terms | Fetched today | Cache | Timeout | Fallback string | Rows |
|---|---|---|---|---|---|---|---|---|
| Open-Meteo geocoding | `https://geocoding-api.open-meteo.com/v1/search?name=&count=&language=` | none | Free tier: 600 per minute, 5,000 per hour, 10,000 per day; **non-commercial use only**, CC-BY 4.0 (`https://open-meteo.com/en/terms`). | 200, JSON with `timezone` `Europe/Amsterdam` | city to place: 30 d | 2 s | `template_helper_lookup_weather_failed` | `lookup.weather`, `lookup.time` |
| Open-Meteo forecast | `https://api.open-meteo.com/v1/forecast?latitude=&longitude=&current=…&daily=…&timezone=auto` | none | same as above | 200, `current` and `daily` blocks returned | 10 min | 2 s | `template_helper_lookup_weather_failed` | `lookup.weather.*` |
| Frankfurter | `https://api.frankfurter.dev/v2/rate/eur/usd` | none | No key, no quota; an abuse rate limit (page text read in part 1). Daily rates. | 200, `{"date","base","quote","rate"}` | 6 h | 2 s | `template_helper_lookup_currency_failed` | `lookup.currency.*` |
| Free Dictionary API | `https://api.dictionaryapi.dev/api/v2/entries/en/<word>` | none | Key and limits **not stated** (not checked). | 200 for `bot`; an unknown word returns a "No Definitions Found" JSON body | 7 d | 2 s | `template_helper_lookup_define_failed` | `lookup.define.*` |
| icanhazdadjoke | `https://icanhazdadjoke.com/` (`Accept: application/json`) | none | A custom `User-Agent` is requested; limits **not stated**. | 200, `{"id","joke","status"}` | none; a pool of 20 jokes is prefetched and served in turn | 2 s | the pool's last joke, or `template_helper_lookup_joke_failed` | `fun.dadjoke`, `lookup.dadjoke` |
| uselessfacts | `https://uselessfacts.jsph.pl/api/v2/facts/random?language=en` | none stated | Limits **not stated**. | 200, `{"id","text","source","language"}` | none; pool of 20 prefetched | 2 s | `template_helper_lookup_fact_failed` | `fun.fact`, `lookup.fact` |
| MyMemory | `https://api.mymemory.translated.net/get?q=&langpair=en\|nl` | none | 5,000 characters per day anonymous, 50,000 with `de=<email>`; `q` at most 500 bytes (part 1). | 200, `responseData.translatedText` returned | 1 d per text and pair | 3 s | `template_helper_lookup_translate_failed` | `lookup.translate.*` |
| Twitch Helix (user, stream, game, follower) | the bot's own client (`ITwitchUsersApi`, `ITwitchStreamsApi`, `ITwitchChannelsApi`, `ITwitchGamesApi`) | the bot's token | Helix rate limit through the existing client | not fetched (needs a live token; not run in this lane) | 60 s | 2 s | `template_helper_lookup_twitch_failed` | `lookup.twitch.*`, `lookup.game.*` |

Rules for all lookups:

1. A lookup never blocks chat longer than its timeout; it renders its fallback string (channel language) instead.
2. Every provider sits behind one interface in `Helpers/Lookups/` (for example `IWeatherClient`); a test stubs the
   `HttpMessageHandler` with the lookup fixture of section 5. No test calls a live service.
3. A client sends `User-Agent: NomNomzBot/<version>`; the Cached rows use `HybridCache` keyed by source and query.
4. The poller limits per source are enforced per process, not per channel, so 1,000 channels cannot exceed the
   provider limit. Open-Meteo's 600 per minute is the tightest: the 10-minute cache keeps a busy bot far below it.

**OPEN DECISION D3 (Stoney): Open-Meteo is free for non-commercial use only.** NomNomzBot is PolyForm Noncommercial and self-hosted
bots are free, but the project also has a hosted option (`saas` mode, run by NoMercy Labs only; nobody else may earn money with NomNomzBot).
A hosted, paid use is commercial under the Open-Meteo terms quoted above. Options: (a) ship weather and local time
on Open-Meteo for self-hosted bots only and switch the two weather rows off in `saas` mode; (b) buy the Open-Meteo
commercial plan (cost and terms **not checked**); (c) pick another source after a new benchmark. Until he picks, L2m
builds the weather client behind `IWeatherClient` so the source can change without touching a template, and builds
(a): the rows render the fallback string when the bot runs in `saas` mode.

## 10. Benchmark

Products: Streamer.bot, Nightbot, StreamElements, Fossabot. Sources were opened on 2026-10-26: a plain fetch worked
for all four (Streamer.bot redirects `/guide/variables` to `/guide/core/variables`; Fossabot was read in a real
browser in part 1, and part 2 found its page also loads by plain fetch). A feature below is in the table only
because the product's page names it; the rows were counted again in part 2 by searching each page's text for the
form's name. Forms read only in part 1 and not re-found are marked "part 1".

| Source | URL |
|---|---|
| Streamer.bot | `https://docs.streamer.bot/guide/core/variables` |
| Nightbot | `https://docs.nightbot.tv/variables` |
| StreamElements | `https://docs.streamelements.com/chatbot/variables` |
| Fossabot | `https://fossabot.com/docs/variables` |

Status: **have** = on `master`; **partial** = part is on `master`; **missing** = a planned catalog row; **not-doing** =
in the Cut table with a reason.

| Feature | Seen in | Source | Ours | Status | Slice |
|---|---|---|---|---|---|
| Date and time in the channel zone | Nightbot `time`, StreamElements `time`, Fossabot `time` | all three pages | `{time.now}`, `{date:now\|…}` (7.2) | have | L1 |
| Date arithmetic and formats | none of the four lists a date calculator | pages above | `{date:<source>\|+1d\|format:…}` | have (we lead) | L1 |
| Count up to and down to a date | Nightbot `countdown`, `countup`; Fossabot `countdown`, `countup` | Nightbot, Fossabot | `{date:…\|until:days}`, `{date:…\|since}` | have | L1 |
| Named counters (read) | Nightbot `count`; StreamElements `count`, `getcount`; Fossabot `count` | all three | `{count.deaths}` | have | L2f |
| Counter next to the stream, per viewer | Streamer.bot `userCounter` (part 1) | Streamer.bot | `{count.deaths.stream}`, `{count.hugs.user}` (7.8) | missing | L2f |
| Random number in a range | Fossabot `randint`; StreamElements `random` | Fossabot, StreamElements | `{random.number:5\|50\|5}` | have | L1 |
| Random phrase from a list | Fossabot `rngphrase` | Fossabot | `{pick:a\|b\|c}`, `{list.pick:snacks}` | have | L1 |
| Math in a template | StreamElements `math`; Fossabot `math`; Streamer.bot `$math()$` | all three pages | `{math:…}` (7.4) | missing | L2b |
| Text length, parse | Streamer.bot `$length()$`, `$parse()$` | Streamer.bot | `{text.length:…}`, `{num.parse:…}` (7.3, 7.4; both names checked in the catalog) | missing | L2a, L2b |
| Repeat text n times | StreamElements `repeat`; Fossabot `repeat` | both | `{text.repeat:3\|ha}` | missing | L2a |
| URL encode and decode | StreamElements `pathescape`, `queryescape`; Fossabot `urlencode`, `urldecode`, `pathencode`, `pathdecode` | both | `{text.url.encode:…}`, `{text.url.decode:…}`, `{text.path.encode:…}` (7.3) | missing | L2a |
| Conditional output | StreamElements `if` | StreamElements | `{if:…\|a\|b}`, `{switch:…}` (7.5) | missing | L2c |
| Command arguments, query, query string | Nightbot `arguments`, `query`, `querystring`; StreamElements `args`; Fossabot `query`, `querystring`, `index`, `fromindex`, `start:end` | all three | `{args.1}`, `{args.all}`, `{args.2+}`, `{args.2-3}` (7.16) | partial | L2k |
| Raw and escaped input | Streamer.bot `rawInput` | Streamer.bot | `{event.rawMessage}`, `{args.all.urlEncoded}` (7.1, 7.16) | partial | L2k |
| Message id | StreamElements `msgid`; Fossabot `msgid` | both | `{chat.message.id}` (7.14) | missing | L2j |
| Sender and target user | Nightbot `user`, `touser`; StreamElements `sender`, `touser`, `user`; Fossabot `sender`, `user` | all three | `{user.display}`, `{target.display}` | have | L1 |
| User level or rank | Nightbot `userlevel`; StreamElements `user` (points, rank) | both | `{user.role}`, `{user.rank:<board>}` (7.7) | missing | L2e |
| Points name and balance | StreamElements `pointsname`, `user` | StreamElements | `{user.points}`, `{user.points.name}` (7.7) | missing | L2e, L2d |
| Channel, game and title | Nightbot `channel`, `twitch`; StreamElements `channel`, `game`, `title`; Fossabot `channel`, `game`, `title` | all three | `{channel.display}`, `{stream.game}`, `{stream.title}` | have | L1 |
| Uptime and downtime | StreamElements `uptime`; Fossabot `uptime`, `downtime` | both | `{stream.uptime}`, `{stream.last.since}` (7.6) | partial | L2d |
| Follow age and account age | Fossabot `followage`, `accountage` | Fossabot | `{user.followAge}`, `{user.accountAge}` | have | L1 |
| Chatters list or count | Fossabot `chatters` | Fossabot | `{activity.*}` (7.12), `{random.chatter}` | partial | L2i |
| Games played in a stream | Fossabot `gamesplayed` | Fossabot | `{stream.games.thisStream}` (7.6) | missing | L2d |
| Charity progress | Fossabot `charity` | Fossabot | `{charity.*}` (7.10) | missing | L2h |
| Bits and Twitch reward fields | Fossabot `bits`, `twitchreward` | Fossabot | `{event.bits}`, `{event.reward}` | have | L1 |
| Third-party emotes (7TV, BTTV, FFZ, Twitch) | StreamElements, Fossabot `7tvemotes`, `bttvemotes`, `ffzemotes`, `twitchemotes` | both | `{emote.*}` (7.13) | missing | L2j |
| Bot uptime | Fossabot `botuptime` | Fossabot | `{bot.uptime}` (7.17) | missing | L2l |
| Weather | Nightbot `weather`; StreamElements `weather`; Fossabot `weather` | all three | `{lookup.weather:Amsterdam}` (7.19, D3) | missing | L2m |
| Quote of the channel | StreamElements `quote`; Fossabot `quote`, `references` | both | `{random.quote}`, `{quote:12}` (7.5) | missing | L2c |
| Fetch a streamer-owned URL | Nightbot `urlfetch`; StreamElements `customapi`; Fossabot `customapi` | all three | `{custom.<source>.<field>}` (as-built, safer) | have | L1 |
| Global variables and persisted values | Streamer.bot `~name~` globals | Streamer.bot | `{var.<name>}`, `{set:x=5}` (7.5; per-render only, no persistence yet: the persisted form is the counters, 7.8) | partial | L2c |
| Click-to-post social links | Nightbot `x`, `bluesky`, `threads`; Fossabot `x`, `mastodon` | Nightbot, Fossabot | `{link.x:…}`, `{link.bluesky:…}`, `{link.mastodon:…}` (7.14) | missing | L2j |
| Game stats (LoL, TFT, PUBG, Valorant, Steam) | Nightbot, StreamElements, Fossabot | all three | none | not-doing (key and scope, 7.20) | none |
| Last.fm | Nightbot, Fossabot | both | none | not-doing (key per streamer, 7.20) | none |
| `eval` in a template | Nightbot, Fossabot | both | none | not-doing (code in a template; scripts SDK covers it) | none |
| AI answer in a template | StreamElements `ai` | StreamElements | none | not-doing (not an approved source) | none |
| Stock price | StreamElements `stockprice` | StreamElements | none | not-doing (key) | none |
| Set game or title from a template | StreamElements, Fossabot `setgame`, `settitle` | both | pipeline actions | not-doing (an action, not a read) | none |
| Currency conversion, dictionary, translation, joke, fact | none of the four lists them | pages above | `lookup.*`, `fun.*` (7.19, 7.18) | missing (we lead) | L2m |
| Literal braces / escape | Streamer.bot `\` escapes (part 1) | Streamer.bot | `\{`, `{{` (D1) | missing | L1 |

That is 40 rows. Rows marked "(we lead)" are forms none of the four products lists in the pages read; they come
from Stoney's 18 groups, not from the benchmark. Every **missing** and **partial** row names its lane; every
**not-doing** row has its reason.

## 11. SDK and surfaces

Rule one-parameterized-read-not-many-endpoints: a family of reads is one endpoint and one typed SDK call, never one
per item. Helpers are template forms. The 1,069 rows add **no** REST route and **no** SDK method per helper.

**One SDK call.** Today the SDK has `format(template, values)` only (a plain `{name}` fill,
`SdkRuntimeSurface.cs:665-667`). L1 adds

```ts
nnz.template.render(template: string, values?: Partial<NnzTemplateValues>): Promise<string>
```

`NnzTemplateKey` is a union written by a `TemplateHelperTypeScript` writer that reflects the registry (the
`StreamMetricTypeScript` pattern), and `NnzTemplateValues` maps a seed key to its value type. A new helper is a new
registry row; it never adds a method. `format` stays for plain fills.

**One automation call.** `POST api/v1/templates/render` with `{ template, context?, values? }` returns `{ text }`.
It runs under the channel's own scope and the same limits (depth 8, 4 KB). `GET api/v1/templates/helpers?context=`
(as-built, `TemplatesController.cs:46`) lists the rows.

**Surfaces.** Every place where a user writes a template resolves it through the one resolver. The list was found
by searching `server/src` for `TemplateResolver` and `ITemplateResolver`.

| Surface | File | Today | Plan |
|---|---|---|---|
| Command reply | `Commands/CommandInvoker.cs` | resolver | same resolver, new parser |
| Builtin reply | `Commands/Builtins/BuiltinResponseComposer.cs` | resolver | same |
| Event response | `Platform/Eventing/EventResponseExecutor.cs` | resolver | same |
| Timer | `Commands/Jobs/TimerService.cs` | resolver | same |
| Pipeline engine and actions | `Platform/Pipeline/PipelineEngine.cs`; `Chat/PipelineActions/SendMessageAction.cs`, `SendReplyAction.cs`, `AnnounceAction.cs`; `Tts/PipelineActions/PlayTtsAction.cs`, `TtsSynthesizeAction.cs`; `Discord/PipelineActions/SendDiscordNotificationAction.cs`; `Stream/PipelineActions/ShoutoutAction.cs`; `Music/PipelineActions/PlaylistAddAction.cs`; `PickLists/PipelineActions/PickFromListAction.cs`; `MediaShare/PipelineActions/*` | resolver | same |
| Discord notification | `Discord/DiscordNotificationDispatcher.cs`, `DiscordEmbedMapper.cs` | resolver | same |
| Webhook body | `Webhooks/WebhookBodyTemplateRenderer.cs` | resolver | same |
| Preview | `Platform/Templating/TemplateSampleRenderer.cs` | sample values | uses `Sample` from the registry (section 4) |
| Save-time check | `Platform/Templating/TemplateHelperValidator.cs` | one-level regex | the new parser |
| Scripts SDK | `CustomCode/ScriptHostBridge.cs`, `DevPlatform/SdkRuntimeSurface.cs` | `format` only | add `nnz.template.render` |
| Overlay and widget SDK | `Api/Controllers/OverlaySdkController.cs` | not checked | add `nnz.template.render` (same typed call) |
| Automation API | `Api/Controllers/V1/TemplatesController.cs` | list only (no render route found by `grep -n render`) | add `POST templates/render` |
| Dashboard | the template editors | helper list | grouped by namespace with search (shadcn `Command`); no UI is written in this lane |

**Parity test (every-action-reachable-everywhere).** A test fails when (1) the keys of the registry differ from the
keys in the written `NnzTemplateKey` union; (2) a surface in the table calls anything other than the one resolver
(a source scan like `TemplateHelperCoverageTests` does today); (3) a seed key a surface provides is missing from
`NnzTemplateValues`.

## 12. Voice

Helpers render **text only**. Whatever speaks the text (chat, announce, TTS, Discord) keeps the channel's one bot
voice (rule bot-speaks-in-one-voice). A viewer's own voice is used only when the spoken text is that viewer's own
words (a TTS reward, reading their message); the TTS action decides that, never a helper. For that reason `tts.voice`
is cut (7.20), and the `tts.*` rows in 7.14 are read-only state such as `tts.audioConnected`. No helper takes a voice
argument, and no helper chooses a voice from a requester id.

## 13. Row count

Command (run 2026-10-26 on this file):

```
grep -E '^\| `.*\| L[0-9a-z]+ \|$' .claude/docs/design/spec/template-helpers-catalog.md | grep -vc CUT
```

| Section | Namespaces | Rows |
|---|---|---|
| 7.1 | as-built keys (aliases) | 171 |
| 7.2 | `date` | 14 |
| 7.3 | `text` | 108 |
| 7.4 | `num`, `math`, `meter` | 76 |
| 7.5 | `if`, `switch`, `default`, `chance`, `pick`, `set`, `var`, `include`, `random`, `roll`, `quote` | 62 |
| 7.6 | `stream`, `channel`, `clip` | 92 |
| 7.7 | `user`, `target`, `viewer` | 94 |
| 7.8 | `count`, `list` | 35 |
| 7.9 | `music` | 51 |
| 7.10 | `poll`, `prediction`, `hypetrain`, `goal`, `charity`, `giveaway` | 85 |
| 7.11 | `ad`, `schedule` | 28 |
| 7.12 | `activity` | 24 |
| 7.13 | `emote` | 33 |
| 7.14 | `chat`, `tts`, `link` | 34 |
| 7.15 | `platform` | 29 |
| 7.16 | `args`, `command` | 32 |
| 7.17 | `bot` | 25 |
| 7.18 | `fun` | 36 |
| 7.19 | `lookup`, `custom` | 40 |
| **Total** | | **1069** |

Of the 1,069 rows, 171 are as-built keys (kept as aliases), 14 are as-built date forms and 884 are new forms. The
target of 1,000 or more counts every form a template can use, as-built and new; the new forms alone are 884. The
Cut table (7.20) lists 17 forms that are not counted (6 of them also appear as CUT rows inside 7.6 to 7.18). Three decisions were open; Arc decided them on 2026-10-27 (Stoney can overrule):

- **D1 decided:** keep `{{key}}` output as built (`{{user}}` renders `{ViewerOne}`); `\{` is the literal-brace escape.
- **D2 decided:** `num.currency` always takes an explicit currency code; the form without a code stays cut.
- **D3 decided:** weather uses Open-Meteo behind an interface, on for self-host, off in saas mode (saas mode is RESTRICTED: you may not earn money with NomNomzBot, and that right is reserved to NoMercy Labs; running it for yourself, your friends and your community is always free).
- **L2d decided:** `stream.game.short` and `text.lines.wrap` keep what the lanes built (`" / "` joiner for wrap).

### Parked helper decisions, answered by Stoney on 2026-10-27

Stoney answered the five parked template-helper questions on 2026-10-27. His answers, as given:

- `activity.level`: "Channel sets levels (Recommended)". Each channel picks its own message-count steps in settings; the bot ships defaults the channel can change. Stoney was asked about it as "how active a viewer is (for example 'regular')", so it is a per-viewer level and not the chat messages-per-minute level. Built as `{user.activity.level}` and `{target.activity.level}` (7.7; `{args.<n>.user.activity.level}` follows from the user rows). The channel settings `ViewerCasualFrom` (default 10), `ViewerRegularFrom` (default 100) and `ViewerVeteranFrom` (default 500) are message counts, read and written with the other channel basics. The level is `new`, `casual`, `regular` or `veteran` from the viewer's real message count in the channel. The chat messages-per-minute level was dropped: `{activity.mpm}` stays.
- `include.builtin`: "Keep it (Recommended)". Built as `{include.builtin:<key>}` in 7.5.
- `chat.message.noCommand`: "Keep it (Recommended)". Built as in 7.14.
- `command.*` against `include.command`: "Both, rename include (Recommended)". `{command.*}` keeps the running-command info (7.16). Including another command is `{include.cmd:name}`. The short form `{command.<name>}` is gone, and a data migration (`RewriteIncludeCommandKey`, both migration sets) rewrites every saved `{include.command:...}` to `{include.cmd:...}`. No alias stays.
- X Live helpers: "Park until X Live exists (Recommended)". **Parked.** The catalog has no X Live row and no code reads X Live. When X Live is a platform connection (SHORTCOMINGS-EXECUTION-PLAN S031), the `platform.*` rows in 7.15 get their X Live forms in the same slice.

