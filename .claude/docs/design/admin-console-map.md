# NomNomzBot admin panel: information architecture (S-ADMIN-DIAGNOSE, widened)

Date: 2026-10-30. Author: Arc (researcher worker). Status: scope approved by Stoney on 2026-10-30; page layouts are still proposals.

Stoney's answers (AskUserQuestion, 2026-10-30), which settle section 6:
- G1: "New admin read" (one admin endpoint per channel with sections; support sessions do not pass the channel checks).
- Reasons, verbatim: "Access logs facing the user are only needed when they grant me the ability to act in their name, read only insights should not require this". So read-only admin reads ask for no reason; act-as keeps its reason.
- Channels and Tenants: "Merge".
- Scope: "Yes, build it", in the order of section 5.

Stoney, 2026-10-30, verbatim: "i tried to debug anda_six's issues myself but i have no tools for this like i asked for. impersonation does nothing on the new ui. i need highly detailed and informative and feature rich tools in the admin pannel".
Stoney, 2026-10-30, verbatim, on three layouts for one channel page: "Too short sighted view of the scope".

Arc's reading: the admin panel is the operator's console for the whole platform. It is not one page. Stoney must confirm.

Act as (S-ADMIN-ACT-AS) is out of scope here. Other pages only link to it.

---

## 1. Counts (read from the files this run)

### Dashboard

| What | Count | Source |
|---|---|---|
| Admin route records | 23 of 23 | `dashboard/src/views/dashboard/admin/route.ts` |
| Admin `*View.vue` pages | 23 | same folder |
| Admin components (`components/*.vue`) | 38 | same folder |
| Admin nav entries | 23 of 23 routes | `dashboard/src/layouts/dashboard/adminNavigation.ts:47-87` |
| Nav groups | 5 (activity, people, billing, safety, configuration) | same file |
| Admin routes with a path parameter (a deep link to one channel, person, run) | 0 of 23 | `route.ts` (every path is flat, for example `admin/channels`) |

The only deep link today is a query string: `/admin/tenants?tenant=<id>` (seen in a test path).

### Server API (`server/openapi/v1.json`)

| What | Count |
|---|---|
| All operations | 955 |
| Operations under `/admin/` | 94 (49 GET) |
| Operations under `channels/` | 600 (264 GET) |
| Non-admin GET reads an operator needs (keyword filter: runs, events, logs, usage, limits, status, scopes, integrations, deliveries, moderation, TTS, bot, billing) | 164 |
| Admin endpoints the Vue code calls | 91 of 94, from the S-ADMIN-DIAGNOSE plan entry. Not re-counted: the client builds paths from segments, so a literal grep cannot count it. |

### Database (`server/src/NomNomzBot.Application/Abstractions/Persistence/IApplicationDbContext.cs`)

| What | Count |
|---|---|
| DbSets | 194 |
| DbSets that hold operational data (runs, jobs, events, deliveries, connections, tokens, usage, audit, safety ops, flags) | 63 of 194 (list in section 1.2) |

### 1.1 Key facts behind the map

1. **An admin cannot read a channel's data without acting as someone.** Per-channel reads use `[RequireAction(...)]`. `ActionAuthorizationHandler.cs` checks the user's role in the current channel only. `ActionAuthorizationService.cs` has no platform-admin or support-session path (grep for `Grant|Support|IsAdmin|Platform|Iam` finds only permit grants). This is the root cause of "no tools".
2. **The error budget counts webhook deliveries only.** `AdminService.GetErrorBudgetAsync` (`AdminService.cs:774-856`) reads `OutboundWebhookDeliveries`. Failed pipeline runs, Discord posts, TTS and chat sends are not in it.
3. **Server exceptions are not stored.** Serilog writes to the console and to a rolling file, 30 days (`Program.cs:104-118`). No table holds errors.
4. **A run row has no link to the event that started it.** `PipelineExecution` has `TriggerKind`, `TriggeredByUserId`, `Status`, `DurationMs`, `HostCallCount`, `ErrorMessage`, `StepLogsJson`. It has no `CorrelationId` or event id.
5. **The journal has correlation data, but the API hides it.** `EventJournal` has `CorrelationId`, `CausationId`, `Payload`, `ImpersonationSessionId`. The `EventJournalEntry` DTO has none of these.
6. **Step logs are thin.** `PipelineExecutionStepLog` = `stepIndex, actionType, succeeded, durationMs, errorMessage`. No host-call breakdown, no variables, no limit key.
7. **The failure reason is free text.** "Execution exceeded its time budget." (plan entry S-SCRIPT-TTS-WAIT) is only a string. No field says which limit was hit.
8. **TTS usage has no timing.** `TtsUsageRecord` has `CharacterCount, Provider, VoiceId, OccurredAt`. No synthesis duration. The anda_six failure (7-15 s per clip, plan S-SCRIPT-TTS-WAIT) cannot be seen from data.
9. **The admin "events" feed is a toy.** `AdminController.GetEvents` (`AdminController.cs:330-362`) returns the last 20 `ChannelEvents`, guesses a color from the type name, and shows only HH:mm.
10. **The person desk already queries the journal across channels.** `AdminSupportService.GetPersonHistoryAsync` (`AdminSupportService.cs:292-345`) calls the event journal with `ActorUserId` and a justification. This is the pattern to reuse: audited, justified, cross-tenant reads.
11. **The admin has a live channel.** `AdminHub` (`Hubs/AdminHub.cs`) plus 8+ broadcast handlers (audit, billing, data requests, error budget, event replay, access, status). A live tail can ride on it.
12. **Two pages list channels.** Channels (`admin/channels`, `AdminChannel` DTO) and Tenants (`admin/tenants`, `AdminTenant` DTO) overlap.

### 1.2 Operational DbSets (63) and who can read them today

"Admin" = a cross-tenant admin endpoint exists. "Channel" = only a per-channel endpoint exists (blocked for admins, see fact 1). "None" = no read endpoint found. "Not checked" = not traced this run.

| Family | DbSets | Read today |
|---|---|---|
| Runs (4) | PipelineExecutions | Channel (`channels/{id}/pipeline-executions`, `/{id}`) |
| | PipelineRunStates (suspended runs) | None found |
| | CommandUsages, CommandCooldownStates | Not checked |
| Jobs and queues (6) | ScheduledPipelineTasks | Admin (`admin/jobs`, retry) |
| | AlertQueueEntries | Channel (`event-responses/alert-queue`) |
| | TtsApprovalQueueEntries | Channel (`tts/queue`) |
| | ModerationQueueItems | Channel (`moderation/automod/queue`) |
| | PlatformContentPublishJobs | Admin (`platform/content/publish-jobs/{id}`) |
| | MediaShareRequests | Not checked |
| Events (6) | EventJournals | Channel (`event-store/channels/{id}/journal`); admin only per person and in replay preview |
| | ChannelEvents | Admin (`admin/events`, last 20) |
| | ProjectionCheckpoints | None (projection names only, `admin/eventstore/projections`) |
| | TenantSequences, IdempotencyKeys, EventSubjectKeys | None |
| Deliveries (3) | OutboundWebhookDeliveries | Admin (`admin/webhooks/deliveries`, replay, error budget) |
| | DiscordNotificationDispatches | Channel (`discord/connections/{id}/dispatch-log`) |
| | ChatMessages | Channel (`chat/messages`); bot sends as a filter: not checked |
| Platform links (7) | EventSubSubscriptions | Admin (`admin/eventsub/health`) and channel |
| | EventSubConduits, EventSubConduitShards | None |
| | PlatformConnections | Admin per person (support desk) |
| | ChannelMissingScopes | Caller's own channel only (`twitch/diagnostics/missing-scopes`, no channel id) |
| | ChannelBotAuthorizations, BotAccounts | Channel (`bot/status`); admin for the shared bot (`admin/platform-bot`) |
| Integrations and tokens (11) | IntegrationConnections, IntegrationTokens | Channel (`integrations/status`: `needsReauth`, `decryptFailed`) |
| | DiscordGuildConnections, ObsConnections, VtsConnections | Channel |
| | AutomationApiTokens, InboundWebhookEndpoints, OutboundWebhookEndpoints | Channel |
| | SupporterConnections | Caller only (`supporters/connections`) |
| | CryptoKeys, KeyUsageBindings | None (rotation action only) |
| Sessions (3) | AuthSessions, RefreshTokens, IpcDevModeKeys | None for admins (`system/ipc/keys` is local) |
| Usage and billing (7) | UsageRecords, TtsUsageRecords | Admin (`admin/usage`, latest period only) and channel (`billing/usage`) |
| | TenantLimitOverrides | Admin |
| | TierLimits, EntitlementGrants, Subscriptions, Invoices | Admin (billing pages) |
| Audit and privacy (6) | IamAuditLogs | Admin (`admin/audit`) |
| | ErasureRequests | Admin (data requests page) |
| | ComplianceAuditLogs, DeletionAuditLogs | None found |
| | SecurityNotices | Not checked |
| | ConsentRecords | Caller only (`gdpr/consents`) |
| Safety operations (7) | SpamDetections, NetworkBlocks | Admin (trust and safety) |
| | SpamCampaigns, MassBanBatches, NetworkNukeBatches, RaidAssessments, ModerationHistoryEntries | Channel, or admin per person |
| Flags (2) | FeatureFlags, FeatureFlagOverrides | Admin |
| Streams (1) | Streams | Channel (`analytics/streams`) |

---

## 2. Benchmark: six real operator consoles

Each row names only what I read on the cited page this run.

| # | Product | Source URL (fetched 2026-10-30) | Operator tools that matter for a multi-tenant bot platform |
|---|---|---|---|
| 1 | Stripe Workbench | https://docs.stripe.com/workbench/overview | Overview with "Recent errors". Errors tab groups errors by type, with "Occurrences", path, source, and linked logs. Logs tab filters by date, status, method, endpoint, IP, source, **account the request applied to**, version, error code, error type. Events tab filters by date, delivery status, type with wildcards (`customer.*`), resource; each event shows payload and delivery attempts, with **Resend**. Inspector: enter an object id; see JSON, a "Data map" of related objects, its logs and its events. "Copy link" makes a shareable URL of the current view. Health tab: alerts with 30-day history. |
| 2 | Stripe Connect dashboard | https://docs.stripe.com/connect/dashboard | A list of all connected accounts (the tenants) with status badges (Restricted, In review, Enabled, Rejected). Filter to "accounts that are restricted or have other issues that you can help resolve". Per account: an "Actions required" list at the top of the account page. |
| 3 | Sentry Issues | https://docs.sentry.io/product/issues/ and https://docs.sentry.io/product/issues/issue-details/ | Groups "similar events into issues" by fingerprint. Shows first and last seen, how many users are hit. Tabs: Unresolved, For Review, Regressed, Archived, Escalating. Sort by last seen, first seen, events, users. Saved searches. Issue detail: step through events, stack trace, breadcrumbs (what happened before), tags with distribution, affected users, trace preview, assign, resolve, archive, activity history. |
| 4 | Inngest | https://www.inngest.com/docs/platform/monitor/inspecting-function-runs and https://www.inngest.com/docs/platform/replay | Runs list filtered by status, time and app; search by expression over event and output. Run detail: trigger id, the **event payload that started the run**, status and error, a **step timeline** showing where it failed, each retry's error and timing. Rerun the run, or **rerun from a selected step**. Replay many failed runs by function, time range and status after an incident. |
| 5 | Supabase Logs | https://supabase.com/docs/guides/telemetry/logs | One log page over many sources (API gateway, Postgres, Auth, Storage, Edge Functions, Realtime). Free-text search, filters for level, status, method, path, **user**. Time range from the sidebar or by dragging on the timeline. **Live** mode that fetches new events. Detail panel with raw JSON. Shareable URL with the filters. Export to CSV or JSON. |
| 6 | Vercel Runtime Logs | https://vercel.com/docs/logs/runtime | Filters for time, level, route, host, deployment, resource, status, environment. **Live mode**. Search fields include `requestId`, `traceId`, `sessionId`. Detail sidebar: function name, duration, memory, an **Events** timeline with timing, **Outgoing Requests** made during the run, then the log lines. Share by URL. Plan-based retention. |
| 7 | Twitch Mod View | https://help.twitch.tv/s/article/mod-view (JS-only page; read in a real browser) | Mod Actions log with filters. AutoMod queue. Unban requests. Activity feed (follows, subs, bits, raids) with filters. Community panel: click any user to see account age, sub tier, their messages, mod actions against them, mod comments, suspicious and ban-evader flags, shared-ban status, then actions. "Other channels you mod for" switcher. Layout is kept per browser. |

### 2.1 What the benchmark says we need (derived)

Proposal, not settled. Each line names the products it comes from.

1. **Find anything from one box.** Paste an id or a name, land on the object (Stripe Inspector, Vercel search fields).
2. **Tenant list with "needs help" status** and a filter for "has issues" (Stripe Connect).
3. **Per-tenant drill-down with "actions required" on top** (Stripe Connect).
4. **Run trace:** trigger event, step timeline, the failing step, its retries, timings and outgoing calls (Inngest, Vercel).
5. **Error groups** with counts, first and last seen, how many channels are hit, and status (Sentry, Stripe Errors).
6. **Event explorer** with type wildcards, delivery attempts and resend (Stripe Events).
7. **Live tail** for a channel or the platform (Supabase, Vercel).
8. **Replay** of failed work, one or many (Inngest, Stripe resend; we already have event replay and webhook replay).
9. **Shareable URL for every view and filter** (Stripe "Copy link", Supabase, Vercel).
10. **Person card across channels** (Twitch Mod View community panel; we have the support desk).
11. **Health with history**, not only "now" (Stripe Health, 30 days).
12. **Retention stated** on every log page (Vercel).

---

## 3. The map

### 3.1 Navigation (Proposal, not settled)

Arc's reading: an operator works in this order. Find the thing. See if it is broken. Look at why. Fix or escalate. The groups follow that order.

| # | Group | Pages |
|---|---|---|
| 0 | Everywhere | Search box (Command, Cmd/Ctrl+K) in the admin header; breadcrumb; "Copy link" on every view |
| 1 | Home | Triage home |
| 2 | Channels | Channel list; Channel console (tabs) |
| 3 | People | People list; Person console (tabs) |
| 4 | Runs and errors | Runs explorer; Run detail; Error groups; Error budget |
| 5 | Events | Event explorer; Event detail; Live tail; Event replay |
| 6 | Deliveries | Delivery log (webhooks, Discord, chat sends, alerts) |
| 7 | Jobs | Scheduled jobs; Suspended runs; Background services |
| 8 | Bot and connections | Platform bot; Connections health; EventSub and conduits; Providers |
| 9 | Billing and limits | Billing; Usage; Limit hits |
| 10 | Content and defaults | Content; Platform defaults; Spam defaults; Feature flags |
| 11 | Trust and safety | Trust and safety; Data requests |
| 12 | Security and audit | Audit log; Access; Sessions; Security notices |
| 13 | Platform health | System; Server log |

### 3.2 Deep links (Proposal, not settled)

Every object has one URL. Every row that names an object links to it.

| Object | URL | Opens |
|---|---|---|
| Channel | `/app/admin/channels/:channelId/:tab?` | Channel console, tab `summary` by default |
| Person | `/app/admin/people/:userId/:tab?` | Person console |
| Run | `/app/admin/runs/:runId` | Run detail (also opens as a Sheet over a list) |
| Event | `/app/admin/events/:eventId` | Event detail |
| Error group | `/app/admin/errors/:groupKey` | Error group detail |
| Job | `/app/admin/jobs/:jobId` | Job detail |
| Delivery | `/app/admin/deliveries/:kind/:id` | Delivery detail |

Filters live in the query string, so a copied link reopens the same view. A view preference (density, a hide toggle) is remembered with `useLocalStorage` under `nnz.admin.<page>.<name>` (rule: view preferences remembered).

Link web: a channel lists its runs, events, people and jobs. A run links to its channel, its trigger event, the person who triggered it, its pipeline, its error group, and its job if it was scheduled. An event links to its channel, its actor, and every run and delivery it caused (needs correlation, gap G4). A person links to every channel they own, moderate or appear in.

### 3.3 Shared patterns (shadcn new-york, from `dashboard/src/components/ui`)

Components present today: table, tabs, sheet, command, breadcrumb, badge, card, chart, collapsible, item, empty, skeleton, scroll-area, tooltip, popover, dropdown-menu, dialog, alert-dialog, select, toggle-group, kbd, spinner, sonner.

| Pattern | Built from | Used by |
|---|---|---|
| Object list | table + toggle-group filters + select + input search + pager | every list |
| Object console | breadcrumb + header card (name, status badges, primary action) + tabs | Channel, Person |
| Quick look | sheet from the right, same content as the full page | runs, events, deliveries, jobs |
| Trace | item rows in a vertical timeline, collapsible per step, duration bar | run detail, event detail |
| Health card | card with one status badge, one number, one "why" line, one link | Home, channel summary |
| Danger action | alert-dialog with a reason field and a "who this reaches" preview (the blast-radius pattern already used) | suspend, erase, replay, retry |

Sleak rules apply when built: one primary action per group, scarce accent (the accent marks only the worst problem on the page), concentric radius.

### 3.4 Page tables

Status words: **exists** (built and useful), **thin** (built but missing what an operator needs), **missing** (no page).
Source words: an endpoint or table, or **missing: needs ...** (gap id in section 4).

#### Group 0. Everywhere

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Search (Command palette) | Find any object by name or id. | Grouped hits: channels, people, runs, events, jobs, error groups. Paste a ULID or GUID and jump straight to it. | Each hit opens its page. | Open; open in act-as (link to S-ADMIN-ACT-AS). | missing: needs `GET admin/search?q=&kinds=` (G2) | missing |

#### Group 1. Home

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Triage home (replaces Overview) | Answer "is anything broken right now, and for whom". | Platform health strip (API, DB, EventSub, bot, providers). "Channels that need help" ranked by failures in the last 24 h (runs failed, deliveries failed, EventSub broken, token needs reauth, limit hit). Top error groups. Live channel count, users, events today. | Channel row to channel console summary. Error group to group. Health card to its page. | Refresh; time window. | `admin/stats`, `admin/system` exist. Ranking: missing: needs `GET admin/attention` (G3). Events today uses `ChannelEvents` (fact 9, thin). | thin (Overview: 5 counters) |

#### Group 2. Channels

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Channel list (merge Channels + Tenants) | Find a channel and see its state at a glance. | Name, live, status (active, suspended), tier, mode, owner, members, created, **problem badges** (failing runs, broken EventSub, reauth needed, over limit). Filters: live, status, tier, "has problems". | Row to channel console. | Suspend, reinstate (exist). Act as (link). | `admin/tenants`, `admin/channels` exist. Problem badges: missing (G3). | thin (two pages, no problem signal, fact 12) |
| Channel console: Summary | One screen that says what is wrong with this channel and why. | "Needs attention" list (Stripe-style actions required). Health cards: bot in chat, each platform connection, EventSub topics, integrations, last failed run, usage against each limit, tier. Last 10 failures across runs and deliveries. | Each card to its tab. Each failure to its run or delivery. | Act as (link). Copy link. | missing: needs the admin channel read family (G1) | missing (`ChannelDetailPanel.vue` shows 8 facts only) |
| Channel console: Runs | Every pipeline, command, script and timer run of this channel. | Time, trigger kind, pipeline, who triggered, status, duration, host calls, error. Filters: failures only, pipeline, trigger, time, error text. | Row to run detail. Pipeline name to the Content tab item. | Re-run as test (see run detail). | `channels/{id}/pipeline-executions` (has `failuresOnly`, `pipelineId`). Admin access: G1. | missing |
| Channel console: Events | What happened in this channel, in order. | Journal rows: time, type, source, actor, position. Filters: type (wildcard), source, actor, time. | Row to event detail. Actor to person. | Replay from here (pre-fills Event replay). Live tail toggle. | `event-store/channels/{id}/journal`. Admin access: G1. | missing |
| Channel console: Connections | Is every platform and integration of this channel working. | One row per provider: Twitch, Kick, YouTube, X Live (if wired: not checked), Spotify, Discord, OBS, VTS, TTS provider, inbound and outbound webhooks, automation tokens. For each: connected, account, granted scopes against needed scopes, needs reauth, decrypt failed, last error. Twitch: missing scopes list. Bot: in chat, which account, shared or own. | Discord row to its dispatch log. Webhook row to deliveries. | Ask streamer to reconnect (a notice, not a token action). | `integrations/status`, `bot/status`, `discord/connections`, `obs/connection`, `vts/connection`, `webhooks/*`, `automation/tokens`. Twitch scopes for any channel: missing (G6). Admin access: G1. | missing |
| Channel console: EventSub | Does Twitch reach this channel. | Each subscription: type, version, transport, status, cost, last error, expiry. Conduit shard if conduit mode. | none | Resubscribe one topic (missing action, G7). | `channels/{id}/eventsub/subscriptions`, `admin/eventsub/health`. Shards: missing (G7). | thin (admin page is cross-tenant only) |
| Channel console: Usage and limits | Is the channel hitting a limit, and which. | Every limit: current, limit, safety baseline, tier default, override. Meters by period (script ms, TTS characters, ...). History by day. Limit-hit events. | Limit to the runs it failed. | Set or clear override (exists). Grant tier (exists). | `billing/limits`, `billing/usage` (channel); `admin/tenants/{id}/limits` (admin). History and limit hits: missing (G5, G9). | thin (overrides only, in Tenants) |
| Channel console: Billing | The channel's plan and money. | Tier, grants, invoices, founder badge, invite used. | Invoice to refund dialog. | Grant tier, grant founder, end grant, refund (exist). | `admin/billing/channels/{id}/*` | exists (in Billing page, `BillingChannelPanel.vue`) |
| Channel console: Content | What the channel built. | Commands, pipelines, scripts (with versions and enabled), timers, event responses, widgets, rewards. Each with "last run" and "last failure". | Item to its runs filtered. | none (edits happen in act-as). | `admin/support/tenants/{id}/commands`, `/pipelines` exist. Scripts, timers, event responses, widgets: missing (G1 sections). | thin (support desk lists commands and pipelines) |
| Channel console: Chat and moderation | What the bot and mods did in chat. | Mod log, bans, AutoMod queue, shield state, spam detections, mass-ban batches, raid assessments, bot's own sent messages. | User to person console. | none here (moderation acts in act-as). | `moderation/log`, `moderation/automod/queue`, `moderation/shield`, `moderation/raid-assessments`, `moderation/actions/mass-ban/batches`. Bot sends: not checked. Admin access: G1. | missing |
| Channel console: Queues | What is waiting. | Alert queue, TTS queue and approvals, media share requests, scheduled jobs, suspended runs. | Job to job detail. Suspended run to run detail. | Retry job (exists). | `event-responses/alert-queue`, `tts/queue`, `admin/jobs` (filter by channel: missing). Suspended runs: missing (G8). | missing |
| Channel console: People | Who has a role here. | Owner, members, moderators, roles, effective permissions. | Person to person console. | Act as this person (link). | `admin/tenants/{id}/members`, `channels/{id}/roles/effective/{userId}`. | thin (`TenantMembersPanel.vue`) |
| Channel console: Flags | Which features are on for this channel. | Every flag with its global value and this channel's override. | Flag to Feature flags page. | Set or clear override (exists). | `admin/feature-flags/overrides`, `channels/{id}/features`. | thin (on Feature flags page only) |
| Channel console: Audit | What operators and the system did to this channel. | IAM audit rows for this channel, act-as sessions, replays, retries, limit changes. | Operator to person. | none | `admin/audit?targetBroadcasterId=` exists. | thin (no per-channel entry point) |
| Channel console: Data | Lifecycle actions. | Erase preview, export, remigrate. | none | Export, remigrate, erase (exist, danger dialog). | `admin/tenants/{id}/export`, `/remigrate`, `/erase/preview`, `/erase` | exists (in `TenantActions.vue`) |

#### Group 3. People

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| People list (merge Users + Support people search) | Find a person on any platform. | Name, login, platform, identities, role, channels owned, last active, flags (anonymized, platform staff). | Row to person console. | none | `admin/users`, `admin/support/people` | thin (two pages) |
| Person console: Summary | Who this person is across the platform. | Identities, platform connections, channels owned and moderated, IAM roles, entitlements, trust scores, last seen. | Channel to channel console. | Act as (link). | `admin/support/people/{id}` | exists (support desk, needs a URL) |
| Person console: History | What this person did and what was done to them, everywhere. | Journal rows by actor across channels; moderation history; community standings. | Event to event detail. Channel to console. | none | `admin/support/people/{id}/history` (journal by actor) | exists (needs filters: type, channel, time) |
| Person console: Runs | Runs this person triggered. | Same columns as the runs explorer, filtered by person. | Run detail. | none | missing: runs explorer with `triggeredBy` (G1/G4 family) | missing |
| Person console: Sessions | Where this person is signed in. | Auth sessions, devices, created, last used, act-as sessions on them. | none | Revoke session (missing action). | missing: needs sessions read (G11) | missing |
| Person console: Data and safety | Privacy and safety state. | Consents, data requests, network blocks, cross-channel signals. | Request to data requests. | Network block (exists). | `admin/trust-safety/*`, data requests page. Consents for another person: missing (G12). | thin |

#### Group 4. Runs and errors

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Runs explorer | Every run on the platform. | Time, channel, pipeline, trigger kind, person, status, duration, host calls, failure kind, limit hit, error. Filters: channel, status, failure kind, trigger, pipeline, person, time, error text. | Run detail. Channel. Person. Error group. | Bulk replay of failed runs (Inngest replay) is a later slice. | missing: needs `GET admin/runs` (G4) | missing |
| Run detail | Why this run failed, step by step. | Header: channel, pipeline and version, trigger kind, trigger event, person, start, duration, status, failure kind, limit key and value. Step timeline: each step with action type, duration bar, result, error. Inside a script step: each host call (name, duration, result), so a 12 s `tts.speak` wait shows at once. Inputs and variables (redacted where secret). | Trigger event. Pipeline in Content tab. Error group. Job. | Re-run as a test run (uses the existing `pipelines/{id}/test-run` under act-as or an admin variant: decide). Copy link. | Detail exists per channel (`pipeline-executions/{id}`); trigger link, host calls, failure kind, limit key: missing (G4, G5). | missing |
| Error groups | Group the same failure across channels. | Group key (normalized message plus failure kind plus action type), count, channels hit, first and last seen, trend, status (open, resolved, regressed). Sources: runs, deliveries, Discord dispatches, TTS failures, EventSub errors, server exceptions. | Group to its runs and deliveries. Channel list for the group. | Mark resolved; note. | missing: needs `GET admin/errors` (G10) | missing |
| Error budget | Success rate per channel against a target. | Today: webhook deliveries only (fact 2). Proposed: one row per channel with per-source rates (runs, webhooks, Discord, TTS, chat sends). | Channel. Source to its list. | none | `admin/error-budget` (webhooks only) | thin |

#### Group 5. Events

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Event explorer | Search the event journal across channels. | Time, channel, type, source, actor, correlation. Filters: channel, type with wildcard (`channel.chat.*`), source, actor, time, correlation id. | Event detail. Channel. Person. | Pre-fill Event replay. | missing: needs `GET admin/journal` (G4 family). Per-channel journal exists. | missing |
| Event detail | One event and everything it caused. | Payload (read-only, internally audited), actor, on-behalf-of, impersonation session, correlation and causation chain, the runs, deliveries, alerts and chat sends it caused. | Each caused item. | Replay this event to one projection. | missing: journal DTO lacks payload and correlation (fact 5, G4) | missing |
| Live tail | Watch events and failures as they happen. | Streaming rows for one channel or the platform, with pause. | Row to detail. | Pause, filter. | missing: needs an `AdminHub` group per channel (G13) | missing |
| Event replay | Rebuild a projection from the journal. | Scope, count preview, history. | Channel. | Preview, execute (exist). | `admin/eventstore/*` | exists |
| Projections | Is every read model up to date. | Projection, checkpoint position, lag against the journal head, last error. | Replay pre-filled. | none | missing: `ProjectionCheckpoints` has no read (G14) | missing |

#### Group 6. Deliveries

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Delivery log (one page, a kind filter) | Did the bot's output arrive. | Kind (webhook, Discord post, chat send, alert, TTS), channel, target, status, attempt, response code, duration, error, time. | Delivery detail. Channel. Trigger event. | Replay a webhook (exists). Resend a Discord post (missing). | Webhooks: `admin/webhooks/deliveries` exists. Discord: channel only. Chat sends, alerts, TTS: missing (G15). | thin (webhooks only) |

#### Group 7. Jobs

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Scheduled jobs | Delayed pipeline work. | Channel, pipeline, due, fired, status, who. Filters: channel, status (missing). | Channel. Run it produced (missing link). | Retry (exists). | `admin/jobs` | exists (no filters) |
| Suspended runs | Runs waiting for an event or a timeout. | Channel, pipeline, step, waiting for, timeout at, runtime so far. | Run detail. | Cancel; resume (missing actions). | missing: `PipelineRunStates` has no read (G8) | missing |
| Background services | Is every worker running. | Each hosted service: running, last tick, last error, queue depth. | none | none | missing: needs a status contract (G16). `admin/system` lists service health names and status only. | missing |

#### Group 8. Bot and connections

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Platform bot | The shared bot account. | Connection, account, channels using it. | Channels list filtered "uses shared bot". | Reconnect by device flow (exists). | `admin/platform-bot/*` | exists |
| Connections health (cross-tenant) | Which channels have a broken link. | One row per channel and provider (Twitch, Kick, YouTube, Spotify, Discord, OBS, VTS, TTS, webhooks): needs reauth, decrypt failed, missing scopes, last error. | Channel console Connections tab. | none | missing: needs the admin connections read (G6) | missing |
| EventSub and conduits | Does Twitch reach the platform. | Per channel topic status (exists). Conduit and shard state, transport, last reconnect (missing). | Channel EventSub tab. | none | `admin/eventsub/health`; shards missing (G7) | thin |
| Providers | App credentials per provider. | Twitch, Spotify, Kick, Discord, YouTube credentials. | none | Save, clear (exist). | `admin/providers` | exists |

#### Group 9. Billing and limits

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Billing | Tiers, invites, priced units, per-channel billing. | As today. | Channel console Billing tab. | As today. | `admin/billing/*` | exists |
| Usage | Use and cost per channel. | Latest period meters and cost; TTS characters. Proposed: pick a period; history by day. | Channel console Usage tab. | none | `admin/usage` (latest period only) | thin |
| Limit hits | Who hits which limit, and how often. | Limit key, channel, count of hits, last hit, tier, current limit. | Runs that failed on it. | Set override (exists, in channel). | missing: needs a limit-hit record (G5, G9) | missing |
| TTS performance | Is a voice or provider slow. | Per provider and voice: count, median and p95 synthesis time, failures. | Channels using the voice. | none | missing: `TtsUsageRecord` has no duration (fact 8, G17) | missing |

#### Group 10. Content and defaults

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Content | Platform content channels install. | Definitions, versions, publish jobs. | Publish job. | Draft, publish, retire (exist). | `platform/content/*` | exists |
| Platform defaults | Defaults for actions, replies, events, voice. | As today, with blast radius. | none | Edit with preview (exist). | `admin/platform-defaults/*` | exists |
| Spam defaults | Default spam policy. | As today. | none | Save (exists). | `admin/spam-defense/defaults` | exists |
| Feature flags | Flags and per-channel overrides. | As today, with blast radius. | Channel console Flags tab. | Set, override (exist). | `admin/feature-flags/*` | exists |

#### Group 11. Trust and safety

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Trust and safety | Review queue, cross-channel signals, network blocks. | As today. | Person console. Channel console. | Confirm, overturn, block, lift (exist). | `admin/trust-safety/*` | exists (person links missing) |
| Data requests | Export and erasure requests. | As today. | Person (hashed by design). | Retry (exists). | data requests endpoints | exists |

#### Group 12. Security and audit

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| Audit log | Every operator action. | IAM checks with filters (exist). Proposed: add compliance and deletion audit as sources. | Operator person. Target channel. | none | `admin/audit` (IAM only). Compliance and deletion logs: missing (G12). | thin |
| Access | Staff principals and roles. | As today. | Person. | Assign, revoke, deactivate (exist). Act as moves out (S-ADMIN-ACT-AS). | `platform/iam/*`, `admin/access/*` | exists |
| Sessions | Sign-in and act-as sessions across the platform. | User, created, last used, device, act-as flag, expiry. | Person. | Revoke (missing). | missing (G11) | missing |
| Security notices | Notices the platform raised to channels. | Channel, kind, time, dismissed. | Channel. | none | missing: `SecurityNotices` admin read (not checked, G18) | missing |
| Encryption key | Re-wrap data keys. | As today. | none | Rotate (exists). | `admin/security/rotate-encryption-key` | exists (inside System) |

#### Group 13. Platform health

| Page | Purpose | Shows | Drill-downs | Actions | Source | Status |
|---|---|---|---|---|---|---|
| System | The running process. | Health per service, version, memory, CPU, updates, totals. Proposed: 30-day health history (Stripe Health). | none | Check for updates (exists). | `admin/system`, `admin/health` | exists (no history) |
| Server log | Server warnings and errors without SSH. | Level, time, message, channel id and correlation id when present, exception. Filters: level, text, channel, time. Live mode. Retention stated (30 days, `Program.cs:116`). | Channel. Run (by correlation). | none | missing: no store (fact 3, G10) | missing |

### 3.5 Page count of the map

Counted row by row from the tables in 3.4 (57 rows).

| Group | Rows | exists | thin | missing |
|---|---|---|---|---|
| 0 Everywhere | 1 | 0 | 0 | 1 |
| 1 Home | 1 | 0 | 1 | 0 |
| 2 Channels | 15 | 2 | 7 | 6 |
| 3 People | 6 | 2 | 2 | 2 |
| 4 Runs and errors | 4 | 0 | 1 | 3 |
| 5 Events | 5 | 1 | 0 | 4 |
| 6 Deliveries | 1 | 0 | 1 | 0 |
| 7 Jobs | 3 | 1 | 0 | 2 |
| 8 Bot and connections | 4 | 2 | 1 | 1 |
| 9 Billing and limits | 4 | 1 | 1 | 2 |
| 10 Content and defaults | 4 | 4 | 0 | 0 |
| 11 Trust and safety | 2 | 2 | 0 | 0 |
| 12 Security and audit | 5 | 2 | 1 | 2 |
| 13 Platform health | 2 | 1 | 0 | 1 |
| **Total** | **57** | **18** | **15** | **24** |

The 18 that exist are mostly configuration pages. The 24 missing pages are mostly diagnosis pages: runs, events, connections, errors, deliveries, queues.

---

## 4. Gap list: what the map needs that has no server source

Rule applied: one parameterized read per family, each item a small registered class (`one-parameterized-read-not-many-endpoints`). Names use `NomNomzBot.*`. Read-only admin reads take no justification (Stoney, 2026-10-30) and are gated by the platform-admin permission; the operator's IAM audit still records them internally, and nothing about them is shown to the streamer. A reason is asked only when an action grants acting in the user's name (act-as).

| Id | Gap | Needs (Proposal, not settled) | Unblocks |
|---|---|---|---|
| G1 | Admins cannot read one channel's data (fact 1). | `GET admin/channels/{broadcasterId}/diagnostics?sections=summary,runs,events,connections,eventsub,usage,limits,content,moderation,queues,flags`. One endpoint, no justification (read-only). Each section is a class implementing `NomNomzBot.Application.Admin.Diagnostics.IChannelDiagnosticSection` that wraps the existing channel query service (for example `IPipelineExecutionQueryService`). Section paging passes through. Decided 2026-10-30: this admin read family, not support sessions passing `ActionAuthorizationService`. | Every Channel console tab |
| G2 | No global search. | `GET admin/search?q=&kinds=channel,person,run,event,job,error`. Each kind is a registered `IAdminSearchProvider`. An id paste resolves directly. | Search |
| G3 | No "needs attention" signal. | A `summary` section in G1, plus `GET admin/attention?window=24h` that ranks channels by failure counts per source. Each source is a registered `IAttentionSignal` (runs failed, deliveries failed, EventSub errors, reauth needed, limit hits). | Home, channel list badges |
| G4 | No cross-tenant runs or journal, no run-to-event link, journal DTO hides payload and correlation (facts 4, 5). | (a) `CorrelationId` and `TriggerEventId` columns on `PipelineExecution` (both migration sets). (b) `GET admin/runs?channel=&status=&failureKind=&trigger=&pipelineId=&triggeredBy=&from=&to=&q=` and `GET admin/runs/{id}`. (c) `GET admin/journal?channel=&type=&source=&actor=&correlation=&from=&to=` and `GET admin/journal/{eventId}?includePayload=true` returning correlation, causation, impersonation session and caused items. | Runs explorer, Run detail, Event explorer, Event detail, Person runs |
| G5 | Failure reason is free text; no limit key; no host-call spans (facts 6, 7). | On the run row: `FailureKind` (enum: limit, timeout, host-call error, script error, permission, platform API error, canceled) and `LimitKey` plus `LimitValue`. In step logs: a `hostCalls` list (name, duration ms, ok, error). Both migration sets. | Run detail, Limit hits, Error groups |
| G6 | Connection health only for the caller's own channel; Twitch scope diagnostics have no channel id. | A `connections` section in G1 and `GET admin/connections?provider=&state=needsReauth,decryptFailed,missingScopes` across channels. Covers every integrated provider: Twitch, Kick, YouTube, Spotify, Discord, OBS, VTS, TTS providers, webhooks, automation tokens (rule: external-api-full-coverage). | Connections tab, Connections health |
| G7 | No conduit or shard read; no resubscribe action. | Conduit and shard state inside `admin/eventsub/health` (an option, not a new route). A `POST admin/channels/{id}/eventsub/resubscribe?type=` action with audit. | EventSub pages |
| G8 | Suspended runs have no read. | A `queues` section in G1 and a `status=suspended` option on `admin/runs` reading `PipelineRunStates`. Cancel and resume actions with audit. | Suspended runs, Queues tab |
| G9 | No usage history and no limit-hit record. | A `period` and `granularity=day` option on `admin/usage`. A limit-hit row written when a limit refuses work (channel, limit key, value, time, run id). | Usage, Limit hits |
| G10 | No error store and no grouping (fact 3). | A Serilog sink that writes Warning and above to a bounded table (with channel id and correlation from the log context), and `GET admin/errors?source=runs,deliveries,discord,tts,eventsub,server&status=&from=&to=` that groups by a fingerprint. Each source is a registered `IErrorSource`. | Error groups, Server log, Error budget widening |
| G11 | No session read or revoke. | `GET admin/sessions?user=&actAs=` and `DELETE admin/sessions/{id}` with audit. | Sessions, Person sessions |
| G12 | Compliance and deletion audit, and another person's consents, have no admin read. | A `source=iam,compliance,deletion` option on `admin/audit`. Consents as a section of the person read. | Audit log, Person data |
| G13 | No live tail. | An `AdminHub` group per channel (`admin-channel:{id}`) fed by the journal append and by run completion, with the same IAM check as the hub. | Live tail, live Channel console |
| G14 | Projection lag unknown. | Checkpoint position and journal head in `admin/eventstore/projections` (an option on the existing read). | Projections |
| G15 | Only webhook deliveries are visible across channels. | `GET admin/deliveries?kind=webhook,discord,chat,alert,tts&status=&channel=`. Each kind is a registered `IDeliverySource`. This route replaces the webhook-only route. | Delivery log |
| G16 | No worker status. | A `NomNomzBot.Application.Platform.IWorkerStatusReporter` each hosted service updates (last tick, last error, queue depth), read through `admin/system?include=workers`. | Background services |
| G17 | TTS synthesis time not recorded (fact 8). | `SynthesisMs` and `Failed` on `TtsUsageRecord` (both migration sets). A `tts` option on `admin/usage`. | TTS performance, anda_six diagnosis |
| G18 | Security notices: admin read not checked. | Check first. If missing, a section in G1 and a cross-tenant option on the audit family. | Security notices |

---

## 5. Build order

Proposal, not settled. One slice = one page, one tab, or one server source. Server first where the source is missing. The order follows "most useful for debugging a streamer's failure first": the anda_six case (runs fail on a time budget while a TTS call is slow) must be fully visible after slice 6.

| # | Slice | Kind | Needs | Done when |
|---|---|---|---|---|
| 0 | Decide G1: admin diagnostics family, or read-only support session in `ActionAuthorizationService`. | Decision (Stoney) | none | Stoney picks one. |
| 1 | S-ADMIN-DIAG-READ: G1 endpoint with sections `summary` and `runs` (list and detail), no reason, internal audit. | Server | 0 | A test reads another channel's failed runs as a platform admin with no reason asked, and the internal audit row exists; a non-admin gets 403. |
| 2 | S-ADMIN-CHANNEL-CONSOLE: routes `/app/admin/channels/:channelId/:tab?`, header, tabs, breadcrumb, Copy link; merge Channels and Tenants lists into one. | UI | 1 | Opening a channel URL after reload shows the same tab; the old two pages redirect. |
| 3 | S-ADMIN-RUNS-TAB: Runs tab and Run detail sheet with the step timeline. | UI | 1 | anda_six's failed runs show step, duration and error text without act-as. |
| 4 | S-RUN-FAILURE-SHAPE: G5 (failure kind, limit key and value, host-call spans), both migration sets. | Server | none | A test run that times out on a slow host call records `FailureKind=timeout`, the limit key, and the host call with its duration. |
| 5 | S-TTS-TIMING: G17 (synthesis ms on TTS usage), both migration sets. | Server | none | A synthesized clip records its duration; a failed one records `Failed`. |
| 6 | Run detail shows host calls, failure kind and limit; Usage and limits tab (current values plus G9 history). | UI + server (G9) | 3, 4, 5 | The run page says "Hit limit X at Y; 12.3 s spent in tts.speak". |
| 7 | S-ADMIN-CONNECTIONS: G6 section plus Connections tab, all providers. | Server then UI | 1 | Each provider row shows reauth, decrypt and scope state for any channel. |
| 8 | EventSub tab plus G7 shard state and resubscribe. | Server then UI | 1 | A broken topic shows its last error, and resubscribe fixes it with an audit row. |
| 9 | S-RUN-CORRELATION plus journal section: G4 (a) and the Events tab and Event detail with caused items. | Server then UI | 1 | From a run, one click opens its trigger event, and back. |
| 10 | Summary tab plus G3 attention signals. | Server then UI | 3, 6, 7, 8 | The summary lists every current problem with a link to its cause. |
| 11 | Home triage with ranked channels. | UI | 10 | The worst channel is first, with its reason. |
| 12 | Runs explorer (cross-tenant), G4 (b). | Server then UI | 4, 9 | Filter "failed, limit, last 24 h" across all channels works and links to each run. |
| 13 | Error groups, G10 (sink plus grouping). | Server then UI | 4, 12 | The same failure in 3 channels shows as one group with count 3 channels. |
| 14 | Search, G2. | Server then UI | 2, 12 | Pasting a run id or a channel name lands on its page. |
| 15 | People console (merge Users and Support), URLs, History filters, Runs by person. | UI (+ G4 option) | 12 | A person URL opens the same tab after reload. |
| 16 | Delivery log, G15. | Server then UI | 9 | A failed Discord post and a failed webhook show on one page. |
| 17 | Queues tab and Suspended runs, G8. | Server then UI | 1 | A waiting run shows what it waits for; cancel works with audit. |
| 18 | Live tail, G13. | Server then UI | 9 | A new event in a test channel appears within 2 s without reload. |
| 19 | Content tab sections (scripts, timers, event responses, widgets) with last run and last failure. | Server then UI | 1, 3 | Each item links to its runs. |
| 20 | Chat and moderation tab. | UI on G1 sections | 1 | Mod log, AutoMod queue and raid assessments show for any channel. |
| 21 | Error budget widened to all sources. | Server then UI | 13 | Each channel shows per-source success rates. |
| 22 | Server log page. | UI | 13 | Warnings filter by channel and level, with live mode. |
| 23 | Sessions, G11. | Server then UI | 15 | Revoking a session signs that device out, with audit. |
| 24 | Audit sources, G12; Security notices, G18. | Server then UI | none | Compliance and deletion rows show on the Audit page. |
| 25 | Projections lag, G14; Background services, G16; System health history. | Server then UI | none | Each projection shows its lag; each worker shows last tick. |

Slices 1 to 6 answer Stoney's anda_six case. Slices 7 to 11 make one channel fully diagnosable. Slices 12 to 18 make the platform searchable and live. The rest complete the console.

---

## 6. Open questions only Stoney can answer

1. G1: a new admin read family, or a read-only support session that passes the channel action check? (Arc's reading: the family is narrower and safer; the session reuses more.)
2. Must every admin read of one channel ask for a reason (as the person desk does today), or only reads of message text and event payloads?
3. Merge Channels and Tenants into one Channels list? (Arc's reading: yes; they show the same channels.)
