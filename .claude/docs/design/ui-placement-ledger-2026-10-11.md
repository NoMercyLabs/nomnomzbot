# UI placement ledger (2026-10-11)

This ledger lists every feature of the bot, its place today, and its place in the new layout from `ui-restructure-research-2026-10-11.md` section 3. It places things into that layout. It does not redesign it. Floors use the user-visible role names (Lead Moderator, never Super Mod). Slice IDs name the plan slice that builds the move. A dash means nothing changes. Sources: the L1 app census (routes), the L2 server census (operations, built-ins, overlay kinds, event types), `builtins_raw.txt` and `eventsub_types.txt`.

## Pages

One row per today route (46 including Admin) and per participant page (6).

| Today | Today group | New group | New page | New tab | Read | Manage | Slice |
|---|---|---|---|---|---|---|---|
| Dashboard | Home | Home | Home | Streamer: Prep (offline) and Live (on air); Moderator: Mod home; Viewer: My channels | Everyone | per card | S-IA-ROLE-HOMES |
| Chat | Chat | Live | Chat | One channel (Shield mode toggle in header) | Moderator | Moderator | S-IA-NAV |
| MultiChat | Chat | Live | Chat | All my channels | Moderator | Moderator | S-IA-NAV |
| Commands | Chat | Build | Commands | Custom; Built-in | Moderator | Moderator | S-IA-NAV |
| ChatTriggers | Chat | Build | Triggers & Timers | Chat triggers | Moderator | Moderator | S-IA-TRIGGERS |
| VoiceTriggers | Chat | Build | Triggers & Timers | Voice triggers | Moderator | Moderator | S-IA-TRIGGERS |
| EventResponses | Chat | Build | Event Responses | Responses | Moderator | Moderator | S-IA-TRIGGERS |
| Timers | Chat | Build | Triggers & Timers | Timers | Moderator | Moderator | S-IA-TRIGGERS |
| Quotes | Chat | Build | Library | Quotes | Moderator | Moderator | S-IA-LIBRARY |
| PickLists | Chat | Build | Library | Random responses | Moderator | Moderator | S-IA-LIBRARY |
| Pipelines | Chat | Build | Pipelines | List; Editor | Moderator | Moderator | S-IA-NAV |
| Widgets (sidebar: Overlays) | Chat | Build | Overlays | Gallery; My overlays | Moderator | Moderator | S-IA-GALLERY |
| CodeScripts | Chat | Build | Scripts | the editor itself (no list page) | Moderator | Moderator | S-IA-SCRIPTS |
| Moderation | Moderation | People | People | Overview | Moderator | read-only | S-IA-PEOPLE |
| ModerationQueue | Moderation | People | People | Queue | Moderator | Moderator | S-IA-PEOPLE |
| ModerationRules | Moderation | People | People | Rules | Moderator | Lead Moderator | S-IA-PEOPLE |
| ModerationHistory | Moderation | People | People | History | Moderator | read-only | S-IA-PEOPLE |
| Rewards | Loyalty | Loyalty | Channel Points | Rewards; Redemptions | Moderator | Moderator | S-IA-NAV |
| Economy | Loyalty | Loyalty | Economy | Currency; Store; Savings jars; Leaderboards | Moderator | Moderator | S-IA-NAV |
| Games | Loyalty | Loyalty | Games | Games; Live sessions | Moderator | Moderator | S-IA-NAV |
| Giveaways | Loyalty | Loyalty | Giveaways | Active; History | Moderator | Moderator | S-IA-NAV |
| Music | Music | Music & Voice | Music | Now playing; Settings | Moderator | Moderator | S-IA-MEDIA |
| SongRequests | Music | Music & Voice | Music | Song requests | Moderator | Moderator | S-IA-MEDIA |
| SoundClips | Music | Build | Library | Sounds | Moderator | Moderator | S-IA-LIBRARY |
| Assets | Music | Build | Library | Files | Moderator | Moderator | S-IA-LIBRARY |
| MediaShare | Music | Music & Voice | Media Share | Queue; Settings | Moderator | Moderator | S-IA-MEDIA |
| Tts | Music | Music & Voice | TTS | Live; Voices; Rules; Provider | Moderator | Moderator (Provider tab: Editor) | S-IA-MEDIA |
| Alerts | Stream | Build | Event Responses | Alert queue | Moderator | Moderator | S-IA-TRIGGERS |
| Schedule | Stream | Live | Stream | Schedule | Moderator | Moderator | S-IA-STREAM |
| Analytics | Stream | Live | Analytics | Stream; Chat; Growth | Moderator | read-only | S-IA-NAV |
| Community | Community | People | People | Viewers | Moderator | Moderator | S-IA-PEOPLE |
| Discord | Connect | Connect | Discord | - | Moderator | Lead Moderator (UI label "Super Mod" is wrong) | S-IA-NAV |
| Webhooks | Connect | Connect | Webhooks | - (generic webhooks only; Ko-fi moves to Supporters) | Broadcaster | Broadcaster | S-IA-SETUP |
| Federation | Connect | Connect | Federation | - | Broadcaster | Broadcaster | - |
| CustomEvents | Connect | Connect | Custom Events | - | Moderator | Editor | - |
| Supporters | Connect | Connect | Supporters | - (owns Ko-fi and the other supporter feeds) | Moderator | Broadcaster | S-IA-SETUP |
| Obs | Connect | Live | OBS | Live control; Advanced (connection form moves to Integrations) | Moderator | Moderator | S-IA-NAV, S-IA-SETUP |
| Vts | Connect | Live | VTube Studio | Hotkeys; Expressions (connection moves to Integrations) | Moderator | Moderator | S-IA-NAV, S-IA-SETUP |
| Automation | Connect | Connect | API & devices | API tokens; Stream Deck pairing; device codes | Editor | Broadcaster | S-IA-NAV |
| Bundles | Setup | Build | Gallery | Bundles | Moderator | Moderator | S-IA-GALLERY |
| Integrations | Setup | Setup | Integrations | - (the one home of every credential) | Broadcaster | Broadcaster | S-IA-SETUP |
| Roles | Setup | People | People | Roles (with Individual permissions) | Moderator | Broadcaster (assign Moderator and VIP: Broadcaster, `ActionDefinitionSeeder.cs:236-237`) | S-IA-PEOPLE |
| Features | Setup | Setup | Features | - | Broadcaster | Broadcaster | - |
| Settings | Setup | Setup | Settings | General; Language & fonts; Notifications; Bot behavior; Privacy; Danger zone (Billing in saas mode only, a RESTRICTED option) | Moderator | Broadcaster | S-IA-SETUP |
| MyData | Setup | Outside the sidebar: profile menu | My data | - | Everyone | Everyone | S-IA-NAV |
| Admin | Admin (isAdmin) | Outside the sidebar: profile menu and Ctrl+K | Admin | 23 tabs unchanged; entering swaps the whole chrome | Platform admin | Platform admin | S-IA-NAV |
| Participant: My channel | Viewer shell | Viewer shell (kept separate) | My channels | cards, one per channel | Everyone | - | S-IA-ROLE-HOMES |
| Participant: Now playing | Viewer shell | Viewer shell (kept separate) | Now playing | per channel | Everyone | - | - |
| Participant: Leaderboards | Viewer shell | Viewer shell (kept separate) | Leaderboards | per channel | Everyone | - | - |
| Participant: Points and store | Viewer shell | Viewer shell (kept separate) | Points and store | per channel | Everyone | - | - |
| Participant: Games | Viewer shell | Viewer shell (kept separate) | Games | per channel | Everyone | - | - |
| Participant: Me | Viewer shell | Viewer shell (kept separate) | Me | TTS voice, pronouns, linked accounts, my standing; My data also in the profile menu | Everyone | - | S-IA-ROLE-HOMES |

## Server operations without a screen

The 150 operations with no app call (mode A) plus the 30 with a client method but no screen caller (mode B). Rows are grouped by tag. "No screen" is used only for device APIs, webhook receivers, OBS or overlay pages, OAuth callbacks, SDK endpoints and similar, with the reason given. Every other row gets a place and a slice (S-IA-GAPS when a new screen or control is needed).

| Operation | Tag | New place (group / page / tab) | Reason if no screen | Slice |
|---|---|---|---|---|
| GET /admin/audit (A) | Admin | Admin / Audit | - | S-IA-GAPS |
| POST /admin/security/rotate-encryption-key (A) | Admin | Admin / System (encryption key rotation) | - | S-IA-GAPS |
| GET /admin/tenants (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| POST /admin/tenants/{broadcasterId}/erase (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| GET /admin/tenants/{broadcasterId}/erase/preview (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| GET /admin/tenants/{broadcasterId}/export (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| GET /admin/tenants/{broadcasterId}/limits (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| PUT /admin/tenants/{broadcasterId}/limits (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| DELETE /admin/tenants/{broadcasterId}/limits/{limitKey} (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| GET /admin/tenants/{broadcasterId}/members (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| POST /admin/tenants/{broadcasterId}/remigrate (A) | Admin | Admin / Tenants | - | S-IA-GAPS |
| GET /assets/file/{channelId}/{name} (A) | Assets | no screen | media file delivery to OBS pages and players | - |
| GET /auth/identities (A) | Auth | Profile menu / Linked accounts (also Viewer shell: Me) | - | S-IA-GAPS |
| DELETE /auth/identities/{identityId} (A) | Auth | Profile menu / Linked accounts (also Viewer shell: Me) | - | S-IA-GAPS |
| POST /auth/identities/{identityId}/primary (A) | Auth | Profile menu / Linked accounts (also Viewer shell: Me) | - | S-IA-GAPS |
| POST /auth/identities/{provider}/link (A) | Auth | Profile menu / Linked accounts (also Viewer shell: Me) | - | S-IA-GAPS |
| POST /auth/identities/{provider}/link/poll (A) | Auth | Profile menu / Linked accounts (also Viewer shell: Me) | - | S-IA-GAPS |
| POST /auth/impersonation/exit (A) | Auth | Profile menu / Exit impersonation (the only control that shows the admin) | - | S-IA-GAPS |
| POST /auth/logout/all (A) | Auth | Profile menu / Sign out on all devices | - | S-IA-GAPS |
| POST /auth/refresh (B) | Auth | no screen | token refresh used by the API client | - |
| GET /auth/twitch (B) | Auth | no screen | OAuth start or callback (launcher, not a screen) | - |
| DELETE /auth/twitch/bot (B) | Auth | Setup / Integrations / Bot account (disconnect) | - | S-IA-SETUP |
| GET /auth/twitch/bot (B) | Auth | Setup / Integrations / Bot account (disconnect) | - | S-IA-SETUP |
| GET /auth/twitch/callback (B) | Auth | no screen | OAuth start or callback (launcher, not a screen) | - |
| POST /auth/twitch/callback (B) | Auth | no screen | OAuth start or callback (launcher, not a screen) | - |
| POST /auth/twitch/channels/{channelId}/bot/device (A) | Auth | no screen | device API: bot-account device-code start (the Integrations bot card is the screen side) | - |
| POST /auth/twitch/device (A) | Auth | no screen | device API: device-code sign-in start | - |
| POST /auth/twitch/device/poll (A) | Auth | no screen | device API: device-code sign-in poll (the sign-in button Use a device code instead calls the provider form, AuthApi.kt:96) | - |
| GET /auth/{provider}/authorize (B) | Auth | no screen | OAuth start or callback (launcher, not a screen) | - |
| GET /auth/{provider}/callback (A) | Auth | no screen | OAuth callback | - |
| POST /auth/{provider}/device/poll (A) | Auth | no screen | device API: device-code sign-in poll (the sign-in button Use a device code instead calls the provider form, AuthApi.kt:96) | - |
| GET /automation/pair/device/approve (A) | Automation | Connect / API & devices / Approve a device | - | S-IA-GAPS |
| POST /automation/pair/device/approve (A) | Automation | Connect / API & devices / Approve a device | - | S-IA-GAPS |
| GET /channels/{channelId}/automation/events/catalog (A) | Automation | Live / Deck / Edit (event picker for a button) | - | S-IA-DECK |
| POST /automation/v1/chat (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/commands (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/info (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| POST /automation/v1/invoke (A) | Automation data plane | no screen | data plane for external clients; the Deck is its main caller (Stream Deck plugin) | S-IA-DECK |
| GET /automation/v1/music/devices (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/music/now-playing (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/music/playlists (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/inputs (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/scene-items (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/scene-transitions (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/scenes (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/source-filters (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| GET /automation/v1/obs/state (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| POST /automation/v1/pair (A) | Automation data plane | no screen | device API: external client pairing (the pairing UI is Connect / API & devices) | - |
| POST /automation/v1/pair/device/init (A) | Automation data plane | no screen | device API: external client pairing (the pairing UI is Connect / API & devices) | - |
| POST /automation/v1/pair/device/poll (A) | Automation data plane | no screen | device API: external client pairing (the pairing UI is Connect / API & devices) | - |
| GET /automation/v1/pipelines (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| POST /automation/v1/refresh (A) | Automation data plane | no screen | data plane for external clients (Stream Deck, scripts); not a dashboard surface | - |
| POST /billing/webhooks/stripe (A) | Billing | no screen | webhook receiver (Stripe) | - |
| GET /channels (B) | Channels | Shell header / Channel switcher and Ctrl+K | - | - |
| POST /channels (B) | Channels | Shell header / Channel switcher and Ctrl+K | - | - |
| POST /channels/moderated/{twitchBroadcasterId}/enter (A) | Channels | Shell header / Channel switcher and Ctrl+K (enter a moderated channel) | - | S-IA-PALETTE |
| GET /channels/{channelId}/delete-preview (A) | Channels | Setup / Settings / Danger zone | - | S-IA-GAPS |
| GET /channels/{channelId}/overlay-token (A) | Channels | Build / Overlays / My overlays (OBS URL on each overlay) | - | S-IA-GALLERY |
| POST /channels/{channelId}/restore (A) | Channels | Setup / Settings / Danger zone | - | S-IA-GAPS |
| GET /channels/{channelId}/chat-polls/{pollId} (A) | ChatPolls | Live / Stream / Interactions (poll detail) | - | S-IA-STREAM |
| GET /channels/{channelId}/community/me/standing (A) | Community | Viewer shell / Me / My standing | - | S-IA-ROLE-HOMES |
| POST /code-scripts/{id}/versions (B) | Custom Code | Build / Scripts / Editor (save version) | - | S-IA-SCRIPTS |
| GET /sdk/event-catalog (A) | Developer SDK | no screen | SDK endpoint (script editor typings and event catalogue; editor use at CodeScriptsController.kt:278 per the census note) | - |
| GET /sdk/types.d.ts (B) | Developer SDK | no screen | SDK endpoint (script editor typings and event catalogue; editor use at CodeScriptsController.kt:278 per the census note) | - |
| DELETE /channels/{channelId}/discord/connections/{connectionId} (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| GET /channels/{channelId}/discord/connections/{connectionId} (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| GET /channels/{channelId}/discord/connections/{connectionId}/blast-radius (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| GET /channels/{channelId}/discord/connections/{connectionId}/guild (B) | Discord | Connect / Discord | - | S-IA-GAPS |
| GET /channels/{channelId}/discord/connections/{connectionId}/guild/channels/postable (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| GET /channels/{channelId}/discord/connections/{connectionId}/guild/roles/assignable (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| POST /channels/{channelId}/discord/roles/{roleId}/opt-in (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| POST /channels/{channelId}/discord/roles/{roleId}/opt-out (A) | Discord | Connect / Discord | - | S-IA-GAPS |
| POST /discord/interactions (A) | Discord | no screen | webhook receiver (Discord interactions) | - |
| GET /channels/{channelId}/economy/accounts/{viewerUserId} (A) | Economy - Currency | People / Viewers / Viewer card (points) | - | S-IA-PEOPLE |
| POST /channels/{channelId}/economy/games/consent (A) | Economy - Games | Viewer shell / Games / Consent prompt | - | S-IA-ROLE-HOMES |
| GET /channels/{channelId}/egress-allowlist (A) | EgressAllowlist | Build / Scripts / Outbound hosts panel in the editor | - | S-IA-GAPS |
| POST /channels/{channelId}/egress-allowlist (A) | EgressAllowlist | Build / Scripts / Outbound hosts panel in the editor | - | S-IA-GAPS |
| DELETE /channels/{channelId}/egress-allowlist/{allowlistId} (A) | EgressAllowlist | Build / Scripts / Outbound hosts panel in the editor | - | S-IA-GAPS |
| GET /channels/{channelId}/egress-allowlist/{allowlistId}/blast-radius (A) | EgressAllowlist | Build / Scripts / Outbound hosts panel in the editor | - | S-IA-GAPS |
| PUT /channels/{channelId}/egress-allowlist/{allowlistId}/enabled (A) | EgressAllowlist | Build / Scripts / Outbound hosts panel in the editor | - | S-IA-GAPS |
| GET /channels/{channelId}/event-responses/overlay (A) | EventResponses | no screen | overlay data read by the OBS alert page | - |
| POST /event-store/channels/{channelId}/import-legacy (A) | EventStore | Build / Gallery / Import (legacy bot data) | - | S-IA-STARTER |
| POST /event-store/channels/{channelId}/replay (A) | EventStore | Setup / Settings / Danger zone (replay events) | - | S-IA-GAPS |
| DELETE /channels/{channelId}/eventsub/subscriptions/{id} (A) | EventSub | Setup / Integrations / Twitch (subscription list) | - | S-IA-GAPS |
| GET /federation/peers/{peerId} (A) | Federation | Connect / Federation / Peer detail | - | S-IA-GAPS |
| GET /gdpr/requests/{id} (B) | GDPR | Profile menu / My data / Request status | - | S-IA-GAPS |
| POST /channels/{channelId}/import/streamelements (A) | Import | Build / Gallery / Import from StreamElements (first-run step too) | - | S-IA-STARTER |
| GET /integrations/discord/callback (A) | Integration OAuth | no screen | OAuth callback | - |
| GET /integrations/{provider}/callback (A) | Integration OAuth | no screen | OAuth callback | - |
| GET /channels/{channelId}/integrations (A) | Integrations | Setup / Integrations | - | S-IA-SETUP |
| DELETE /channels/{channelId}/integrations/spotify/credentials (B) | Integrations | Setup / Integrations | - | S-IA-SETUP |
| DELETE /channels/{channelId}/integrations/{integrationId} (B) | Integrations | Setup / Integrations | - | S-IA-SETUP |
| GET /channels/{channelId}/live-ops/raids/scoring-rules (A) | LiveOps | Live / Stream / Interactions (raid scoring rules) | - | S-IA-STREAM |
| PUT /channels/{channelId}/live-ops/raids/scoring-rules (A) | LiveOps | Live / Stream / Interactions (raid scoring rules) | - | S-IA-STREAM |
| GET /channels/{channelId}/live-ops/schedule/icalendar/subscribe (A) | LiveOps | Live / Stream / Schedule (iCal subscribe link) | - | S-IA-STREAM |
| GET /channels/{channelId}/marketplace/items (A) | Marketplace | Build / Gallery / Templates | - | S-IA-GALLERY |
| GET /channels/{channelId}/marketplace/items/{itemId} (B) | Marketplace | Build / Gallery / Templates | - | S-IA-GALLERY |
| GET /channels/{channelId}/marketplace/submissions/{submissionId} (B) | Marketplace | Build / Gallery / Templates | - | S-IA-GALLERY |
| GET /media-share/next (B) | Media Share | Music & Voice / Media Share / Queue (play next) | - | S-IA-MEDIA |
| GET /media-share/queue (A) | Media Share | Music & Voice / Media Share / Queue | - | S-IA-MEDIA |
| POST /channels/{channelId}/moderation/actions/mass-ban (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/actions/mass-ban/batches (A) | Moderation | People / Overview / Mass-ban batches | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/actions/mass-ban/channels (A) | Moderation | People / Overview / Mass-ban batches | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/actions/mass-ban/opt-ins (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| DELETE /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin} (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| PUT /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin} (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/bans (A) | Moderation | People / History / Bans and unban | - | S-IA-GAPS |
| DELETE /channels/{channelId}/moderation/bans/{userId} (A) | Moderation | People / History / Bans and unban | - | S-IA-GAPS |
| POST /channels/{channelId}/moderation/chat-filters/test (A) | Moderation | People / Rules / Test a filter | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/mass-ban-opt-in (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| PUT /channels/{channelId}/moderation/mass-ban-opt-in (A) | Moderation | People / Rules / Mass-ban settings and opt-in | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/protection-status (A) | Moderation | People / Overview / Standing of the chat | - | S-IA-PEOPLE |
| GET /channels/{channelId}/moderation/shoutout-overrides (B) | Moderation | People / Viewers / Viewer card (per-user settings) | - | S-IA-PEOPLE |
| GET /channels/{channelId}/spam-defense/lockdown (A) | Moderation | People / Rules / Spam defense lockdown (also a Deck button) | - | S-IA-PEOPLE |
| POST /channels/{channelId}/spam-defense/lockdown (A) | Moderation | People / Rules / Spam defense lockdown (also a Deck button) | - | S-IA-PEOPLE |
| POST /channels/{channelId}/spam-defense/lockdown/end (A) | Moderation | People / Rules / Spam defense lockdown (also a Deck button) | - | S-IA-PEOPLE |
| GET /channels/{channelId}/music/now-playing (A) | Music | Music & Voice / Music / Now playing | - | S-IA-MEDIA |
| GET /oauth-relay (A) | OAuthRelay | no screen | OAuth redirect relay for the browser; no user surface | - |
| POST /channels/{channelId}/obs/request (A) | OBS | Live / OBS / Advanced (raw request) | - | S-IA-GAPS |
| GET /overlay/bundle/{widgetId} (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /overlay/manifest (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /overlay/now-playing (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /overlay/queue (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /overlay/spotify-token (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /overlay/storage/{key} (A) | Overlay | no screen | OBS overlay page endpoint (browser source) | - |
| GET /channels/{channelId}/permissions (A) | Permissions | People / Roles / Individual permissions | - | S-IA-PEOPLE |
| POST /channels/{channelId}/permissions (A) | Permissions | People / Roles / Individual permissions | - | S-IA-PEOPLE |
| DELETE /channels/{channelId}/permissions/{userId} (A) | Permissions | People / Roles / Individual permissions | - | S-IA-PEOPLE |
| DELETE /channels/{channelId}/permits/{userId} (A) | Permits | People / Roles / Individual permissions (permits) | - | S-IA-PEOPLE |
| POST /channels/{channelId}/pipelines/validate (A) | Pipelines | Build / Pipelines / Editor (validate) | - | S-IA-GAPS |
| GET /channels/{channelId}/pipelines/{id}/platform-default (A) | Pipelines | Build / Pipelines / List (restore platform default) | - | S-IA-GAPS |
| POST /channels/{channelId}/pipelines/{id}/platform-default/restore (A) | Pipelines | Build / Pipelines / List (restore platform default) | - | S-IA-GAPS |
| GET /pipelines/options/{kind} (A) | Pipelines | Build / Pipelines / Editor (pickers) | - | S-IA-GAPS |
| GET /platform/analytics/stats (A) | Platform Analytics | Admin / Overview (platform stats) | - | S-IA-GAPS |
| GET /platform/content/audio-assets (A) | Platform Content | Admin / Content | - | S-IA-GAPS |
| POST /platform/content/audio-assets (A) | Platform Content | Admin / Content | - | S-IA-GAPS |
| DELETE /platform/content/audio-assets/{id} (A) | Platform Content | Admin / Content | - | S-IA-GAPS |
| GET /platform/content/definitions/{id}/versions/{versionId} (B) | Platform Content | Admin / Content | - | S-IA-GAPS |
| GET /platform/content/publish-jobs/{id} (B) | Platform Content | Admin / Content | - | S-IA-GAPS |
| GET /admin/platform-defaults/actions/{actionKey}/blast-radius (A) | Platform Defaults | Admin / Platform defaults (blast radius before saving) | - | S-IA-GAPS |
| DELETE /platform/iam/assignments/{assignmentId} (A) | Platform IAM | Admin / IAM | - | S-IA-GAPS |
| POST /platform/iam/principals/{principalId}/deactivate (A) | Platform IAM | Admin / IAM | - | S-IA-GAPS |
| GET /platform/iam/principals/{principalId}/permissions (A) | Platform IAM | Admin / IAM | - | S-IA-GAPS |
| GET /pronouns/me (B) | Pronouns | Viewer shell / Me / Pronouns | - | S-IA-ROLE-HOMES |
| GET /public/sr/by-channel/{channelName} (A) | PublicSongRequest | no screen | public song request page served by the server as plain HTML (paper section 5.3) | - |
| POST /public/sr/by-channel/{channelName} (A) | PublicSongRequest | no screen | public song request page served by the server as plain HTML (paper section 5.3) | - |
| GET /public/sr/{token} (A) | PublicSongRequest | no screen | public song request page served by the server as plain HTML (paper section 5.3) | - |
| POST /public/sr/{token} (A) | PublicSongRequest | no screen | public song request page served by the server as plain HTML (paper section 5.3) | - |
| GET /quotes/random (A) | Quotes | Build / Library / Quotes (random pick) | - | S-IA-GAPS |
| GET /channels/{channelId}/render-manifest (A) | Render Manifest | no screen | overlay and OBS render manifest read by the overlay page | - |
| GET /channels/{channelId}/rewards/leaderboard (B) | Rewards | Loyalty / Economy / Leaderboards | - | S-IA-GAPS |
| GET /channels/{channelId}/roles/effective/me (A) | Roles | People / Roles / Effective permissions (viewer card) | - | S-IA-PEOPLE |
| GET /channels/{channelId}/roles/effective/{userId} (A) | Roles | People / Roles / Effective permissions (viewer card) | - | S-IA-PEOPLE |
| GET /sound-clips/stream/{storageKey} (A) | Sound | no screen | media file delivery to OBS pages and players | - |
| PATCH /channels/{channelId}/stream/game (A) | Stream | Live / Stream / Info & presets | - | S-IA-STREAM |
| GET /channels/{channelId}/stream/status (A) | Stream | Live / Stream / Info & presets | - | S-IA-STREAM |
| PATCH /channels/{channelId}/stream/tags (A) | Stream | Live / Stream / Info & presets | - | S-IA-STREAM |
| PATCH /channels/{channelId}/stream/title (A) | Stream | Live / Stream / Info & presets | - | S-IA-STREAM |
| GET /system/ipc (A) | System | Admin / System / IPC dev keys | - | S-IA-GAPS |
| GET /system/ipc/keys (A) | System | Admin / System / IPC dev keys | - | S-IA-GAPS |
| POST /system/ipc/keys (A) | System | Admin / System / IPC dev keys | - | S-IA-GAPS |
| DELETE /system/ipc/keys/{keyId} (A) | System | Admin / System / IPC dev keys | - | S-IA-GAPS |
| GET /system/setup/bot/oauth-url (B) | System | Setup / Integrations / Bot account | - | S-IA-SETUP |
| GET /system/setup/bot/status (B) | System | Setup / Integrations / Bot account | - | S-IA-SETUP |
| PUT /system/setup/credentials/discord (A) | System | Admin / Providers / Platform credentials | - | S-IA-GAPS |
| PUT /system/setup/credentials/kick (A) | System | Admin / Providers / Platform credentials | - | S-IA-GAPS |
| PUT /system/setup/credentials/spotify (A) | System | Admin / Providers / Platform credentials | - | S-IA-GAPS |
| PUT /system/setup/credentials/youtube (A) | System | Admin / Providers / Platform credentials | - | S-IA-GAPS |
| GET /system/status (A) | System | Home / Stream health and Admin / System | - | S-IA-GAPS |
| DELETE /channels/{channelId}/tts/me/voice (B) | TTS | Viewer shell / Me / TTS voice | - | S-IA-ROLE-HOMES |
| GET /channels/{channelId}/tts/me/voice (B) | TTS | Viewer shell / Me / TTS voice | - | S-IA-ROLE-HOMES |
| PUT /channels/{channelId}/tts/me/voice (B) | TTS | Viewer shell / Me / TTS voice | - | S-IA-ROLE-HOMES |
| POST /channels/{channelId}/tts/voices/assignments/import (A) | TTS | Music & Voice / TTS / Voices | - | S-IA-MEDIA |
| GET /channels/{channelId}/timers/{id}/platform-default (A) | Timers | Build / Triggers & Timers / Timers (restore platform default) | - | S-IA-TRIGGERS |
| POST /channels/{channelId}/timers/{id}/platform-default/restore (A) | Timers | Build / Triggers & Timers / Timers (restore platform default) | - | S-IA-TRIGGERS |
| GET /viewers/{viewerId}/data (A) | Viewer Data | People / Viewers / Viewer card (custom data) | - | S-IA-PEOPLE |
| POST /webhooks/in/{token} (A) | Webhooks | no screen | webhook receiver | - |
| POST /webhooks/kick (A) | Webhooks | no screen | webhook receiver | - |
| POST /channels/{channelId}/widgets/{widgetId}/compile (B) | Widgets | Build / Overlays / My overlays (editor: compile) | - | S-IA-GALLERY |
| GET /channels/{channelId}/widgets/{widgetId}/versions/{versionId} (B) | Widgets | Build / Overlays / My overlays (editor: version) | - | S-IA-GALLERY |

## Built-in commands

Deck button is "yes" when the command takes no viewer input and a streamer would send it on stream. Floor is the default level in code (L2 census).

| Command | Shown in (Commands: Built-in) | Deck button? (yes/no) | Floor if known |
|---|---|---|---|
| !uptime | Build / Commands / Built-in | yes | Everyone |
| !update | Build / Commands / Built-in | no | Everyone |
| !lurk | Build / Commands / Built-in | no | Everyone |
| !unlurk | Build / Commands / Built-in | no | Everyone |
| !accountage | Build / Commands / Built-in | no | Everyone |
| !followage | Build / Commands / Built-in | no | Everyone |
| !quote | Build / Commands / Built-in | no | Everyone |
| !sr | Build / Commands / Built-in | no | Everyone |
| !skip | Build / Commands / Built-in | yes | Everyone |
| !queue | Build / Commands / Built-in | yes | Everyone |
| !volume | Build / Commands / Built-in | no | Everyone |
| !song | Build / Commands / Built-in | yes | Everyone |
| !banger | Build / Commands / Built-in | yes | Everyone |
| !commands | Build / Commands / Built-in | yes | Everyone |
| !help | Build / Commands / Built-in | yes | Everyone |
| !bansong | Build / Commands / Built-in | no | Everyone |
| !leaderboard | Build / Commands / Built-in | yes | Everyone |
| !playlist | Build / Commands / Built-in | yes | Everyone |
| !whisper | Build / Commands / Built-in | no | Moderator |
| !discord | Build / Commands / Built-in | yes | Everyone |
| !coinflip | Build / Commands / Built-in | no | Everyone |
| !dice | Build / Commands / Built-in | no | Everyone |
| !slots | Build / Commands / Built-in | no | Everyone |
| !permit | Build / Commands / Built-in | no | Moderator |
| !unpermit | Build / Commands / Built-in | no | Moderator |
| !whitelist | Build / Commands / Built-in | no | Broadcaster |
| !unwhitelist | Build / Commands / Built-in | no | Broadcaster |
| !allow | Build / Commands / Built-in | no | Lead Moderator |
| !disallow | Build / Commands / Built-in | no | Lead Moderator |
| !voice | Build / Commands / Built-in | no | Everyone |
| !stats | Build / Commands / Built-in | no | Everyone |
| !profile | Build / Commands / Built-in | no | Everyone |
| !media | Build / Commands / Built-in | no | Everyone |
| !forgetme | Build / Commands / Built-in | no | Everyone |
| !mydata | Build / Commands / Built-in | no | Everyone |
| !gdpr | Build / Commands / Built-in | no | Everyone |

## Overlay kinds

| Kind | Page (Build / Overlays) | Deck show-or-hide button (yes) |
|---|---|---|
| alerts | Build / Overlays / My overlays (auto-provisioned, not in gallery browse) | yes |
| goal_bar | Build / Overlays / Gallery | yes |
| labels | Build / Overlays / Gallery | yes |
| drop_game | Build / Overlays / Gallery | yes |
| raffle | Build / Overlays / Gallery | yes |
| heist | Build / Overlays / Gallery | yes |
| crash | Build / Overlays / Gallery | yes |
| event_ticker | Build / Overlays / Gallery | yes |
| chat_box | Build / Overlays / Gallery | yes |
| now_playing | Build / Overlays / Gallery | yes |
| sr_queue | Build / Overlays / Gallery | yes |
| audio | Build / Overlays / My overlays (auto-provisioned, not in gallery browse) | yes |
| tts_caption | Build / Overlays / My overlays (auto-provisioned, not in gallery browse) | yes |
| poll_prediction | Build / Overlays / Gallery | yes |
| redemption_alert | Build / Overlays / Gallery | yes |
| countdown_timer | Build / Overlays / Gallery | yes |
| emote_wall | Build / Overlays / Gallery | yes |
| custom_data | Build / Overlays / Gallery | yes |
| recent_followers | Build / Overlays / Gallery | yes |
| sub_train | Build / Overlays / Gallery | yes |
| socials | Build / Overlays / Gallery | yes |
| top_cheerers | Build / Overlays / Gallery | yes |
| lucky_feather | Build / Overlays / Gallery | yes |

## Events

Platform prefix marks Kick and YouTube types. Keys that are template variables (user, stream, channel fields) are not events: they go to the variable picker in the Build editors. Deck test button and Live feed on Home describe the NEW layout (the target), not today. The Event Responses tab names are the groupings by event type.

| Event | Event Responses tab | Deck test button (yes/no) | Live feed on Home (yes/no) |
|---|---|---|---|
| channel.ad.break.begin | Ads | yes | yes |
| channel.ad_break.begin | Ads | yes | yes |
| channel.ad_break.end | Ads | yes | yes |
| channel.ad_break.upcoming | Ads | yes | yes |
| channel.ban | Moderation | no | yes |
| channel.bits.use | Cheers & bits | yes | yes |
| channel.channel_points_automatic_reward_redemption.add | Channel points | yes | yes |
| channel.channel_points_custom_reward.add | Channel points | yes | yes |
| channel.channel_points_custom_reward.remove | Channel points | yes | yes |
| channel.channel_points_custom_reward.update | Channel points | yes | yes |
| channel.channel_points_custom_reward_redemption.add | Channel points | yes | yes |
| channel.channel_points_custom_reward_redemption.update | Channel points | yes | yes |
| channel.charity_campaign.donate | Charity | yes | yes |
| channel.charity_campaign.progress | Charity | yes | yes |
| channel.charity_campaign.start | Charity | yes | yes |
| channel.charity_campaign.stop | Charity | yes | yes |
| channel.chat.clear | Moderation | no | yes |
| channel.chat.clear_user_messages | Moderation | no | yes |
| channel.chat.message | Chat | no | no |
| channel.chat.message_delete | Moderation | no | yes |
| channel.chat.notification | Chat | no | no |
| channel.chat.user_message_hold | Moderation | no | yes |
| channel.chat.user_message_update | Chat | no | no |
| channel.chat_settings.update | Chat | no | no |
| channel.cheer | Cheers & bits | yes | yes |
| channel.custom_power_up_redemption.add | Channel points | yes | yes |
| channel.delete | not an event: a broadcaster-only permission action (PermissionService.cs:78) | no | no |
| channel.display | not an event: template variable (variable picker in Build editors) | no | no |
| channel.follow | Follows & subs | yes | yes |
| channel.followed | Follows & subs | yes | yes |
| channel.game | not an event: template variable (variable picker in Build editors) | no | no |
| channel.goal.begin | Hype train & goals | yes | yes |
| channel.goal.end | Hype train & goals | yes | yes |
| channel.goal.progress | Hype train & goals | yes | yes |
| channel.guest_star_guest.update | Guest Star | no | no |
| channel.guest_star_session.begin | Guest Star | no | no |
| channel.guest_star_session.end | Guest Star | no | no |
| channel.guest_star_settings.update | Guest Star | no | no |
| channel.hype_train.begin | Hype train & goals | yes | yes |
| channel.hype_train.end | Hype train & goals | yes | yes |
| channel.hype_train.progress | Hype train & goals | yes | yes |
| channel.id | not an event: template variable (variable picker in Build editors) | no | no |
| channel.moderate | Moderation | no | yes |
| channel.moderator.add | Moderation | no | yes |
| channel.moderator.remove | Moderation | no | yes |
| channel.name | not an event: template variable (variable picker in Build editors) | no | no |
| channel.points.custom.reward.redemption.add | Channel points | yes | yes |
| channel.points.custom.reward.redemption.update | Channel points | yes | yes |
| channel.poll.begin | Polls & predictions | yes | yes |
| channel.poll.end | Polls & predictions | yes | yes |
| channel.poll.progress | Polls & predictions | yes | yes |
| channel.prediction.begin | Polls & predictions | yes | yes |
| channel.prediction.end | Polls & predictions | yes | yes |
| channel.prediction.lock | Polls & predictions | yes | yes |
| channel.prediction.progress | Polls & predictions | yes | yes |
| channel.raid | Raids & shoutouts | yes | yes |
| channel.raid.out | Raids & shoutouts | yes | yes |
| channel.raid.outgoing | Raids & shoutouts | yes | yes |
| channel.raid.start | Raids & shoutouts | yes | yes |
| channel.reward.redemption.updated | Channel points | yes | yes |
| channel.shared_chat.begin | Chat | no | no |
| channel.shared_chat.end | Chat | no | no |
| channel.shared_chat.update | Chat | no | no |
| channel.shield_mode.begin | Moderation | no | yes |
| channel.shield_mode.end | Moderation | no | yes |
| channel.shoutout.create | Raids & shoutouts | yes | yes |
| channel.shoutout.receive | Raids & shoutouts | yes | yes |
| channel.subscribe | Follows & subs | yes | yes |
| channel.subscription.end | Follows & subs | yes | yes |
| channel.subscription.gift | Follows & subs | yes | yes |
| channel.subscription.gift.anonymous | Follows & subs | yes | yes |
| channel.subscription.gift.received | Follows & subs | yes | yes |
| channel.subscription.gift.received.anonymous | Follows & subs | yes | yes |
| channel.subscription.gifts | Follows & subs | yes | yes |
| channel.subscription.message | Follows & subs | yes | yes |
| channel.subscription.new | Follows & subs | yes | yes |
| channel.subscription.renewal | Follows & subs | yes | yes |
| channel.suspicious_user.message | Moderation | no | yes |
| channel.suspicious_user.update | Moderation | no | yes |
| channel.timeout | Moderation | no | yes |
| channel.title | not an event: template variable (variable picker in Build editors) | no | no |
| channel.transfer | not an event: a broadcaster-only permission action (PermissionService.cs:78) | no | no |
| channel.unban | Moderation | no | yes |
| channel.unban_request.create | Moderation | no | yes |
| channel.unban_request.resolve | Moderation | no | yes |
| channel.update | Stream status | yes | yes |
| channel.url | not an event: template variable (variable picker in Build editors) | no | no |
| channel.vip.add | Moderation | no | yes |
| channel.vip.remove | Moderation | no | yes |
| channel.warning.acknowledge | Moderation | no | yes |
| channel.warning.send | Moderation | no | yes |
| stream.ad.break.began | Ads | yes | yes |
| stream.channel.updated | Stream status | yes | yes |
| stream.game | not an event: template variable (variable picker in Build editors) | no | no |
| stream.guest.star.guest.updated | Guest Star | no | no |
| stream.guest.star.session.began | Guest Star | no | no |
| stream.guest.star.session.ended | Guest Star | no | no |
| stream.guest.star.settings.updated | Guest Star | no | no |
| stream.offline | Stream status | yes | yes |
| stream.online | Stream status | yes | yes |
| stream.raid | Raids & shoutouts | yes | yes |
| stream.shoutout.received | Raids & shoutouts | yes | yes |
| stream.shoutout.sent | Raids & shoutouts | yes | yes |
| stream.title | not an event: template variable (variable picker in Build editors) | no | no |
| stream.uptime | not an event: template variable (variable picker in Build editors) | no | no |
| stream.viewers | not an event: template variable (variable picker in Build editors) | no | no |
| user.bits | not an event: template variable (variable picker in Build editors) | no | no |
| user.displayname | not an event: template variable (variable picker in Build editors) | no | no |
| user.get | not an event: template variable (variable picker in Build editors) | no | no |
| user.id | not an event: template variable (variable picker in Build editors) | no | no |
| user.lastmessage | not an event: template variable (variable picker in Build editors) | no | no |
| user.link | not an event: template variable (variable picker in Build editors) | no | no |
| user.login | not an event: template variable (variable picker in Build editors) | no | no |
| user.lookup | not an event: template variable (variable picker in Build editors) | no | no |
| user.name | not an event: template variable (variable picker in Build editors) | no | no |
| user.object | not an event: template variable (variable picker in Build editors) | no | no |
| user.possessive | not an event: template variable (variable picker in Build editors) | no | no |
| user.pronouns | not an event: template variable (variable picker in Build editors) | no | no |
| user.provider | not an event: template variable (variable picker in Build editors) | no | no |
| user.role | not an event: template variable (variable picker in Build editors) | no | no |
| user.subject | not an event: template variable (variable picker in Build editors) | no | no |
| user.tense | not an event: template variable (variable picker in Build editors) | no | no |
| user.update | Bot account: user-scoped EventSub on the bot identity (EventSubConditionBuilder.cs:156), no channel response | no | no |
| user.whisper.message | Bot account: the bot inbox, subscribed once on the platform plane (BotLifecycleService.cs:157), no channel response | no | no |
| automod.message.hold | Moderation | no | yes |
| automod.message.update | Moderation | no | yes |
| automod.settings.update | Moderation | no | yes |
| automod.terms.update | Moderation | no | yes |
| engagement.first_time_chatter | Engagement (first chatter, returning, streak, modiversary) | yes | yes |
| engagement.modiversary | Engagement (first chatter, returning, streak, modiversary) | yes | yes |
| engagement.returning_chatter | Engagement (first chatter, returning, streak, modiversary) | yes | yes |
| engagement.watch_streak | Engagement (first chatter, returning, streak, modiversary) | yes | yes |
| kick:chat.message.sent | Chat | no | no |
| kick:channel.followed | Follows & subs | yes | yes |
| kick:channel.subscription.new | Follows & subs | yes | yes |
| kick:channel.subscription.renewal | Follows & subs | yes | yes |
| kick:channel.subscription.gifts | Follows & subs | yes | yes |
| kick:channel.reward.redemption.updated | Channel points | yes | yes |
| kick:livestream.status.updated | Stream status | yes | yes |
| kick:livestream.metadata.updated | Stream status | yes | yes |
| kick:moderation.banned | Moderation | no | yes |
| kick:kicks.gifted | Follows & subs | yes | yes |
| youtube:textMessageEvent | Chat | no | no |
| youtube:superChatEvent | Cheers & bits | yes | yes |
| youtube:superStickerEvent | Cheers & bits | yes | yes |
| youtube:newSponsorEvent | Follows & subs | yes | yes |
| youtube:memberMilestoneChatEvent | Follows & subs | yes | yes |
| youtube:membershipGiftingEvent | Follows & subs | yes | yes |
| youtube:giftMembershipReceivedEvent | Follows & subs | yes | yes |

## Counts

| Category | Placed (N) | Total (M) | UNPLACED |
|---|---|---|---|
| Today routes (incl. Admin) | 46 | 46 | 0 |
| Participant pages | 6 | 6 | 0 |
| Server operations with no app call | 150 | 150 | 0 |
| Server operations with a client method but no screen | 30 | 30 | 0 |
| Built-in commands | 36 | 36 | 0 |
| Overlay kinds | 23 | 23 | 0 |
| Event types and keys | 149 | 149 | 0 |

UNPLACED rows: 0.
