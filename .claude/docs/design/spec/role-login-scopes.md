# Role-based login — one sign-in per role, only that role's scopes

Status: **draft, not settled.** Slice S-ROLE-LOGIN-SPEC in `SHORTCOMINGS-EXECUTION-PLAN.md`. No code until Stoney accepts this page.

Owner, verbatim (2026-10-11): "We need a login as user, login as moderator and login as broadcaster for all platforms and require only the permissions needed at login time to execute on behalf of their role within the bot"

## What the code does today (inventory, 2026-10-11)

| Platform | Login flow | Where | Scopes asked | Role choice |
|---|---|---|---|---|
| Twitch | Streamer login, redirect and device code | `Infrastructure/Identity/AuthService.cs:89-97` (`MinimalLoginScopes`) | `user:read:chat`, `user:write:chat`, `user:read:moderated_channels` (no email scope; we never store an email) | No — everyone gets the streamer set |
| Twitch | Re-auth and re-grant | `AuthService.cs:191-209`, `:545-580`; `TwitchDiagnosticsController.cs:106,115` | Minimal set + granted + recorded-missing scopes | No |
| Twitch | Bot account | `AuthService.cs:101-115` | `user:read:chat`, `user:write:chat`, `user:bot`, `chat:read`, `chat:edit`, `user:read:whispers` | n/a (bot) |
| Kick | Login (PKCE) | `Identity/Login/KickLoginProvider.cs:35` | `user:read` | No |
| Kick | Streamer and bot connect | `Integrations/OAuthProviderRegistry.cs:106-142` | `user:read`, `chat:write`, `moderation:ban`, `moderation:chat_message:manage`, `events:subscribe` | No |
| YouTube | Login (Google) | `Identity/Login/GoogleYouTubeLoginProvider.cs:35` | `openid`, `profile` (no email scope) | No |
| YouTube | Connect | `OAuthProviderRegistry.cs:231-236` | `youtube` + `youtube.force-ssl`, or `youtube.readonly` | Capability choice, not a role |
| X | Login (PKCE) | `Identity/Login/TwitterLoginProvider.cs:36` | `users.read`, `tweet.read`, `offline.access` | No |

The app knows only `OAuthFlow { Streamer, Bot }` (`app/.../core/connection/ConnectionProfile.kt:47-59`). There is no role picker.

Moderation already runs on the moderator's own token: the `*AsOperator` calls in `ITwitchModerationApi.cs:39-349` send `moderator_id` = the signed-in user. Today a moderator gets those scopes only later, through the progressive re-grant.

Gaps:
- A viewer who signs in on Twitch grants chat write and the moderated-channels list. That is more than a viewer uses.
- A moderator has to find the re-grant before moderation works.
- Kick, YouTube and X have no role choice at all.

## Proposal, not settled

### The sign-in screen
One screen, three choices, in the order of how many people use them: **Viewer**, **Moderator**, **Broadcaster**. Then the platform buttons (Twitch, Kick, YouTube, X). A returning client with a stored session skips this screen (rule auth-onboarding-vs-login-vs-remembered).

### Scope sets per role (Twitch)

| Role | Scopes | Why each one |
|---|---|---|
| Viewer | none beyond identity | The bot reads the viewer's id and name from the token. Viewer features (own TTS voice, own profile) run on the bot's side. |
| Moderator | `user:read:moderated_channels`, `user:read:chat`, `user:write:chat`, and the `moderator:*` scopes the `*AsOperator` calls use (ban/timeout, delete message, blocked terms, shield mode, unban requests, warnings, suspicious users, automod, announcements, chat settings) | Channel switcher; chat composer; every moderation action runs as the moderator |
| Broadcaster | today's minimal set + every channel scope the enabled features use (`Identity/FeatureScopeMap.cs`) | The bot acts for the channel: broadcast info, raids, polls, predictions, redemptions, VIPs, making the bot a moderator |

The exact `moderator:*` list is built from the code catalogue (`AuthService.cs:183`) at implementation time and checked against the live Twitch docs.

### Kick, YouTube, X
Each platform gets the same three choices. The scope sets come from the platform's live docs, checked before writing (rule verify-current-docs). Where a platform has no moderator scope for a third party, the moderator choice on that platform says so plainly and acts through the broadcaster's connection.

### Changing role later
A viewer made moderator, or a moderator who becomes a broadcaster, gets an in-app prompt. The prompt asks for only the extra scopes, by the redirect flow, in place. No logout (rule never-logout-for-scope-or-schema-changes).

### What the role choice does not do
The choice limits the scopes asked from the platform. It does not grant bot permissions: what a person may do in the bot stays with the channel's role ladder (`roles-permissions.md`). A user who picks Moderator but is not a moderator of any channel sees the empty state with that reason.

## View as — the owner sees the dashboard as a moderator or a viewer

Owner, verbatim (2026-10-16): "I can access it, but do i need a dedicated view as viewer action? I have my full bot ui so I can't test what a normal user sees"

### Decisions
- The channel owner can pick **Moderator** or **Viewer** in the profile menu. Only the owner of the open channel sees the entry.
- The choice is truthful, not cosmetic. The server applies it to every request, so pages, data and writes match what that role really gets.
- The choice can only LOWER the caller's level. It never raises it. A value other than `moderator` or `viewer` is ignored.
- A persistent banner "Viewing as <role>" with one Exit button shows while the choice is on. Exit restores the full role at once.
- The choice is remembered in the tab (`nnz.viewAs`, local storage) so a reload keeps it until Exit.
- It is not impersonation. The caller stays themselves; no token is minted and nothing is journalled as another person.

### Data flow
1. The dashboard stores the role and sends header `X-View-As: moderator|viewer` on every API request (`dashboard/src/api/client.ts` middleware).
2. `HttpViewAsScope` (Infrastructure) reads the header and maps it to a ceiling on the unified ladder: moderator = 10, viewer = 0.
3. `RoleResolver` caps the effective level at `MIN(level, ceiling)` and drops direct capability grants while the ceiling is on. Every Gate-2 check (`[RequireAction]`, `ActionAuthorizationService`) and `GET roles/effective/me` go through it, so the held action keys the dashboard reads are the lowered set.
4. The dashboard nav shows a page only when the caller holds that page's read action key, so the sidebar follows the same server answer.
5. The owner-only chat color write (`ChatIdentityController`) refuses the owner while the ceiling is on.

### What the server enforces
- Gate 2 for every endpoint with `[RequireAction]`: refused above the ceiling (403).
- `roles/effective/me`: reports the lowered level and held keys.
- Raise is impossible: `MIN` with the real level, and only two known values map to a ceiling.
- Not covered: chat commands and background work (no HTTP request, no header) — they keep the real role, as they should. The realtime hub keeps the real role for its push events; the data each page loads still goes through the lowered REST gate.

## Open for Stoney
- Broadcaster: all channel scopes of the enabled features at login (Arc's reading of "at login time"), or today's minimal set plus a prompt per feature?
