// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Seeds the global <c>ActionDefinitions</c> catalogue (roles-permissions §7.1, schema B.3) — the gateable
/// action keys with their default/floor levels, danger tier, and permit-grantability. Without these Gate 2
/// fails closed (403) on every gated route. GLOBAL reference data, no FK dependencies (Order 5). Idempotent:
/// upserts by the natural key <see cref="ActionDefinition.ActionKey"/>, so a re-run adds nothing.
/// <para>
/// Convention: a management-plane row's <c>DefaultLevel</c> is its Twitch base role and equals
/// <c>FloorLevel</c> (<c>Tier = Low</c>, <c>Grant = true</c>) UNLESS the two-level <c>M</c> overload sets a
/// lower floor. For non-destructive reads and reversible, non-destructive writes the default stays at the
/// base role (a lower-standing viewer gets nothing extra out of the box) but the floor drops to
/// <c>Vip(4)</c>, so the broadcaster MAY choose to lower the requirement to a trusted VIP — the override is
/// clamped to <c>[FloorLevel, Broadcaster]</c>. Destructive, irreversible, Twitch-mutating, currency, or
/// role/IAM actions keep <c>Floor = Default</c> at Moderator+. Community-plane rows are
/// <c>Default = Floor = Everyone(0)</c>, <c>Low</c>, grantable. The one row with a HIGHER default than floor
/// (<c>permit:issue</c>) is added explicitly. These rows are the source for the per-spec §5 controller gates.
/// </para>
/// </summary>
public sealed class ActionDefinitionSeeder : ISeeder
{
    private const int Everyone = 0;

    // Unified-ladder rungs below Moderator (AuthorizationLadder: Subscriber=2, Vip=4, Artist=6). Vip(4) is
    // used as the FLOOR for non-destructive reads and reversible writes: the action still DEFAULTS to its
    // Twitch base role, but the broadcaster MAY lower the requirement as far as Vip so a trusted VIP can be
    // let in — abusing these actions cannot cause irreversible harm. Kept in sync with
    // PermissionLevel.Vip.ToLevelValue().
    private const int Vip = 4;
    private const int Mod = 10;
    private const int LeadModerator = 20;
    private const int Editor = 30;
    private const int Broadcaster = 40;

    private readonly IApplicationDbContext _db;

    public ActionDefinitionSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 5;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Dictionary<string, ActionDefinition> existing =
            await _db.ActionDefinitions.ToDictionaryAsync(
                a => a.ActionKey,
                StringComparer.Ordinal,
                ct
            );

        foreach (ActionSeed seed in Catalogue)
        {
            if (existing.TryGetValue(seed.Key, out ActionDefinition? row))
            {
                // The catalogue is authoritative: re-sync plane/floor/tier so a CORRECTED default (e.g. a key that
                // should be Moderator-floored rather than Everyone) takes effect on existing installs, not only on
                // a fresh DB. Channel-specific customisations live in ChannelActionOverrides and are untouched.
                row.Plane = seed.Plane;
                row.DefaultLevel = seed.DefaultLevel;
                row.FloorLevel = seed.FloorLevel;
                row.FloorTier = seed.Tier;
                row.IsGrantableViaPermit = seed.Grant;
            }
            else
            {
                _db.ActionDefinitions.Add(
                    new ActionDefinition
                    {
                        ActionKey = seed.Key,
                        Plane = seed.Plane,
                        DefaultLevel = seed.DefaultLevel,
                        FloorLevel = seed.FloorLevel,
                        FloorTier = seed.Tier,
                        IsGrantableViaPermit = seed.Grant,
                    }
                );
            }
        }
    }

    private static readonly IReadOnlyList<ActionSeed> Catalogue = Build();

    private static List<ActionSeed> Build()
    {
        List<ActionSeed> s = [];

        // ── Management plane (Default = Floor, Tier = Low, Grant = true unless noted; the
        //    M(key, default, floor) overload lowers the floor below the default for actions the broadcaster
        //    may choose to open to a trusted VIP/Sub) ──

        // Default == floor: the action requires `level` and can never be lowered below it.
        void M(string key, int level, DangerTier tier = DangerTier.Low, bool grant = true) =>
            s.Add(new ActionSeed(key, level, level, tier, grant, AuthPlane.Management));

        // Default ≠ floor: the action DEFAULTS to `defaultLevel` (its Twitch base role — nothing extra is
        // granted out of the box) but the broadcaster MAY lower the per-action requirement as far as
        // `floorLevel` via ChannelActionOverride. Used ONLY where abusing the action cannot cause
        // irreversible or serious harm: non-destructive reads and reversible, non-destructive writes.
        void MFloor(
            string key,
            int defaultLevel,
            int floorLevel,
            DangerTier tier = DangerTier.Low,
            bool grant = true
        ) =>
            s.Add(new ActionSeed(key, defaultLevel, floorLevel, tier, grant, AuthPlane.Management));

        // Commands / pipelines / responses / timers — reads (and non-mutating pipeline validation) default to
        // the Moderator base but the broadcaster may lower them to Vip; the writes bundle create/edit/DELETE
        // (real data loss) so they stay at Editor.
        MFloor("commands:read", Mod, Vip);
        M("commands:write", Editor);
        MFloor("commands:builtin:read", Mod, Vip);
        M("commands:builtin:write", Editor);
        MFloor("pipelines:read", Mod, Vip);
        M("pipelines:write", Editor);
        MFloor("pipelines:validate", Mod, Vip);
        MFloor("eventresponses:read", Mod, Vip);
        M("eventresponses:write", Editor);
        MFloor("chattriggers:read", Mod, Vip);
        M("chattriggers:write", Editor);
        MFloor("chatpolls:read", Mod, Vip);
        MFloor("chatpolls:write", Mod, Vip);
        MFloor("timers:read", Mod, Vip);
        M("timers:write", Editor);

        // Bundles (marketplace.md §5) — export/import at Editor (imported code is sandboxed + disabled,
        // destructive actions bound by the importer's own runtime roles, D4); publish is Broadcaster-only.
        // `bundles:publish` is seeded now; its route ships with the marketplace-client slice.
        M("bundles:read", Mod);
        M("bundles:export", Editor);
        M("bundles:import", Editor);
        M("bundles:publish", Broadcaster);

        // Roles & permits & code
        M("roles:read", Mod);
        M("roles:manage", Broadcaster, DangerTier.Critical, grant: false);
        s.Add(
            new ActionSeed(
                "permit:issue",
                Broadcaster,
                Editor,
                DangerTier.Low,
                true,
                AuthPlane.Management
            )
        );
        M("code:script:author", Broadcaster, DangerTier.Critical);

        // Discord (not permit-grantable)
        M("discord:connection:read", Mod, grant: false);
        M("discord:connection:write", LeadModerator, grant: false);
        M("discord:config:read", Mod, grant: false);
        M("discord:config:write", LeadModerator, grant: false);
        M("discord:role:read", Mod, grant: false);
        M("discord:role:write", LeadModerator, grant: false);
        M("discord:optin:write", LeadModerator, grant: false);
        M("discord:dispatch:read", Mod, grant: false);

        // Bot identity — per-channel white-label bot connect/status/disconnect (identity-auth §5). Connecting
        // or severing a bot identity is owner-level and not permit-delegable; reading status is.
        M("channelbot:connect", Broadcaster, grant: false);
        M("channelbot:read", Broadcaster);
        M("channelbot:disconnect", Broadcaster, grant: false);

        // Moderation
        M("moderation:read", Mod);
        M("moderation:queue:read", Mod);
        M("moderation:queue:resolve", Mod);
        M("moderation:action:read", Mod);
        M("moderation:timeout", Mod);
        M("moderation:ban", Mod);
        M("moderation:unban", Mod);
        M("moderation:delete_message", Mod);
        M("moderation:warn", Mod);
        M("moderation:note:write", Mod);
        M("moderation:automod:read", Mod);
        M("moderation:automod:write", LeadModerator);
        M("moderation:filter:read", Mod);
        M("moderation:filter:write", LeadModerator);
        M("moderation:nuke", LeadModerator, DangerTier.Critical, grant: false);
        M("moderation:nuke:read", LeadModerator);
        M("moderation:sharedban:read", LeadModerator);
        M("moderation:sharedban:write", LeadModerator, DangerTier.Critical, grant: false);
        M("moderation:escalation:read", Mod);
        M("moderation:escalation:write", LeadModerator);
        M("moderation:report:read", Mod);
        M("moderation:report:triage", LeadModerator);
        M("moderation:evidence:build", Mod);
        M("moderation:usercontext:read", Mod);
        M("moderation:chat:settings:read", Mod);
        M("moderation:chat:settings:write", Mod);
        M("moderation:shieldmode:read", Mod);
        M("moderation:shieldmode:write", LeadModerator);
        M("chat:announce", Mod);
        // Dashboard chat page (frontend-ia.md §Chat): chat-history read + send-a-message-as-the-bot (REST and
        // DashboardHub). chat:read DEFAULTS to Moderator but is broadcaster-lowerable to Vip (reading chat is
        // non-destructive); chat:send stays Moderator — sending as the bot is not something a VIP should do.
        MFloor("chat:read", Mod, Vip);
        M("chat:send", Mod);
        M("moderation:shoutout", Mod);
        M("moderation:chatcolor:write", Editor);
        M("moderation:vip", Broadcaster);
        M("moderation:moderator:write", Broadcaster, DangerTier.Critical, grant: false);
        M("moderation:unbanrequest:read", Mod);
        M("moderation:unbanrequest:resolve", LeadModerator);
        M("moderation:blocklist:write", LeadModerator);
        M("moderation:suspicioususer:write", LeadModerator);

        // TTS
        // Reads default to the Moderator base, floor Vip (broadcaster may open the presentation config /
        // voice list to a VIP — non-destructive). tts:voice:test triggers audio (spammable) so it stays Mod.
        MFloor("tts:config:read", Mod, Vip);
        M("tts:config:write", Editor);
        MFloor("tts:voice:read", Mod, Vip);
        M("tts:voice:test", Mod);
        M("tts:uservoice:write", Mod);
        M("tts:queue:review", Mod);

        // EventSub / diagnostics / event store
        M("eventsub:read", Mod);
        M("eventsub:subscribe", Editor);
        M("eventsub:unsubscribe", Editor);
        M("twitch:diagnostics:read", Mod);
        M("eventstore:journal:read", Broadcaster);
        M("eventstore:projection:read", Mod);
        M("eventstore:projection:rebuild", Broadcaster);
        M("eventstore:replay:write", Broadcaster);
        M("eventstore:replay:republish", Broadcaster);
        // Portable journal export/import — exporting reads the entire journal; importing mutates it. Both are
        // owner-level and not permit-delegable below Broadcaster.
        M("eventstore:export", Broadcaster, grant: false);
        M("eventstore:import", Broadcaster, DangerTier.Critical, grant: false);
        // One-shot legacy backfill — reads the legacy bot's database and appends its channel history to the journal.
        // Mutates the journal, so it is owner-level and danger-tier Critical, like the portable import.
        M("eventstore:import:legacy", Broadcaster, DangerTier.Critical, grant: false);

        // Music
        M("music:config:write", Editor);
        // music-sr.md §5.1 floors GET config at management/Moderator with no named key; the read key follows
        // the catalogue's *:config:read convention (cf. economy:config:read). Default Moderator, floor Vip —
        // reading config is non-destructive so the broadcaster may open it to a VIP.
        MFloor("music:config:read", Mod, Vip);
        M("music:queue:moderate", Mod);
        M("music:token:read", Editor);
        M("music:token:rotate", Broadcaster, DangerTier.Critical, grant: false);
        M("music:remote:control", Mod);
        M("music:library:write", Editor);

        // Stream / channel / live-ops
        // stream-admin.md §5 floors the stream-info/status/category reads at management entry (i.e. Moderator);
        // stream:read carries that DEFAULT now that Gate 1 is pure entry. Reading stream info is non-destructive,
        // so its floor drops to Vip and the broadcaster may open it to a VIP.
        MFloor("stream:read", Mod, Vip);
        M("stream:preset:write", Editor);
        M("stream:schedule:write", Editor);
        M("channel:title:write", Editor);
        M("channel:game:write", Editor);
        M("channel:tags:write", Editor);
        M("channel:ccl:write", Editor);
        M("channel:language:write", Editor);
        M("channel:brandedcontent:write", Editor);
        M("channel:extensions:write", Editor);
        M("chat:whisper:send", Editor);
        M("live-ops:polls:read", Mod);
        M("live-ops:polls:write", Editor);
        M("live-ops:predictions:read", Mod);
        M("live-ops:predictions:write", Editor);
        M("live-ops:raids:write", Editor);
        M("live-ops:ads:read", Mod);
        M("live-ops:ads:write", Editor);
        M("live-ops:schedule:read", Mod);
        M("live-ops:schedule:write", Editor);
        M("live-ops:marker:create", Mod);
        M("live-ops:clips:write", Mod);

        // Automation API tokens (automation-api.md §5) — external credentials, so the write bundle
        // (create/rotate/revoke) is broadcaster-only Critical and never grantable.
        M("automation:tokens:read", Editor);
        M("automation:tokens:write", Broadcaster, DangerTier.Critical, grant: false);

        // OBS control (obs-control.md §7): config carries a vaulted secret + the bridge credential
        // (write = Critical, never grantable); scene/source control is the Moderator floor while the
        // broadcast bundle (start/stop stream & recording, raw requests) stays broadcaster-only.
        M("obs:config:read", Broadcaster);
        M("obs:config:write", Broadcaster, DangerTier.Critical, grant: false);
        M("obs:control", Mod);
        M("obs:control:broadcast", Broadcaster, DangerTier.Critical);

        // VTube Studio (vtube-studio.md §5): the plugin token is minted by the in-VTS approval, never
        // typed into a form, so the write bundle stays plain (unlike obs:config:write).
        M("vts:config:read", Mod);
        M("vts:config:write", Broadcaster);
        M("vts:control", Mod);

        // Webhooks / widgets / integrations / dashboard / community / setup / analytics
        M("webhooks:inbound:read", Mod);
        M("webhooks:inbound:write", Editor);
        M("webhooks:outbound:read", Mod);
        M("webhooks:outbound:write", Editor);
        MFloor("widget:read", Mod, Vip);
        M("widget:write", Editor);
        M("widget:compile", Editor);
        MFloor("widget:version:read", Mod, Vip);
        M("widget:rollback", Editor);
        M("widget:install", Editor);
        M("integration:read", Mod);
        M("integration:write", Editor);
        M("community:read", Mod);
        // Managing a viewer's trust level is a per-viewer moderation-tier community write.
        M("community:trust:write", Mod);
        MFloor("dashboard:read", Mod, Vip);
        M("setup:write", Broadcaster, grant: false);
        // Per-channel feature enablement (FeaturesController): read at Mod, toggle at the config-write tier.
        M("feature:read", Mod);
        M("feature:write", Editor);
        M("analytics:read", Mod);
        M("analytics:viewer:read", Mod);

        // Economy (management)
        M("economy:config:read", Mod);
        M("economy:config:write", Editor);
        M("economy:earning-rules:read", Mod);
        M("economy:earning-rules:write", Editor);
        M("economy:earning-rules:delete", Editor);
        M("economy:accounts:read", Mod);
        M("economy:games:write", Broadcaster);

        // Live games (live-games.md §5): starting/cancelling an overlay round is Moderator live-ops;
        // the per-game config (odds/bets/enable) stays the Broadcaster's economy:games:write above.
        M("games:session:read", Mod);
        M("games:session:start", Mod);
        M("games:session:cancel", Mod);
        M("economy:catalog:create", Editor);
        M("economy:catalog:update", Editor);
        M("economy:catalog:delete", Editor);
        M("economy:catalog:refund", LeadModerator);
        M("economy:catalog:purchases:read", Mod);
        M("economy:account:freeze", Mod);
        M("economy:account:adjust", Mod);
        M("economy:ledger:read", Mod);
        M("economy:leaderboards:config:read", Mod);
        M("economy:leaderboards:config:write", Editor);
        M("economy:leaderboards:config:delete", Editor);

        // Billing (owner-level control — reads included; mods/editors never see or touch billing)
        M("billing:read", Broadcaster);
        M("billing:manage", Broadcaster);

        // Federation opt-ins (cross-instance sharing — default-deny, LeadModerator gated)
        M("federation:optin:read", LeadModerator);
        M("federation:optin:write", LeadModerator);
        M("federation:optin:delete", LeadModerator);

        // Rewards — reading the reward list defaults to Moderator, floor Vip (non-destructive). Managing /
        // syncing rewards is Twitch-mutating and owner-level, so it stays at Broadcaster.
        MFloor("reward:read", Mod, Vip);
        M("reward:manage", Broadcaster);
        M("reward:sync", Broadcaster);
        M("reward:redemption:read", Mod);
        M("reward:redemption:fulfill", Mod);
        M("reward:redemption:refund", Mod);

        // Quotes (quotes.md §5) — reading + curating (add/edit) DEFAULT to Moderator but the broadcaster MAY
        // lower them to Vip (floor Vip) so a trusted VIP can help build the quote library. Deleting is
        // low-but-real data loss, so quotes:delete stays Moderator with a Moderator FLOOR — it can never be
        // lowered to VIP. The DELETE route carries quotes:delete; POST/PUT carry quotes:write.
        MFloor("quotes:read", Mod, Vip);
        MFloor("quotes:write", Mod, Vip);
        M("quotes:delete", Mod);

        // Pick-lists (the generic {list.pick.<name>} primitive) — mirrors Quotes: reading + curating
        // (create/edit) DEFAULT to Moderator but the broadcaster MAY lower them to Vip (floor Vip) so a trusted
        // VIP can help build lists; deleting is low-but-real data loss, so picklists:delete stays Moderator with
        // a Moderator floor. The DELETE route carries picklists:delete; POST/PUT carry picklists:write.
        MFloor("picklists:read", Mod, Vip);
        MFloor("picklists:write", Mod, Vip);
        M("picklists:delete", Mod);

        // Giveaways (giveaways.md §6) — running a giveaway (open/close/draw/redraw) is live-ops work, so
        // read + write sit at Moderator. The code pools hold VALUABLE SECRETS (game keys), so
        // giveaways:codes:write is Broadcaster with a Broadcaster floor — never delegable below the owner.
        M("giveaways:read", Mod);
        M("giveaways:write", Mod);
        M("giveaways:codes:write", Broadcaster);

        // Custom data sources (custom-events.md §5) — the pipeline-facing external data feeds (HypeRate,
        // Pulsoid, webhooks). Read at Moderator, write (create/update/delete/test) at Editor.
        M("customdata:read", Mod);
        M("customdata:write", Editor);

        // Per-viewer data store (per-viewer-data.md §5) — a viewer's custom key/values (death counters,
        // quest flags). Browsing is mod work; hand-editing what pipelines wrote sits at Editor.
        M("viewerdata:read", Mod);
        M("viewerdata:write", Editor);

        // Engagement triggers (engagement.md §5) — auto-greet/loyalty config. Read at Moderator, toggling
        // the triggers (write) at Editor.
        M("engagement:read", Mod);
        M("engagement:write", Editor);

        // Media share (media-share.md §5) — the viewer clip/video queue. Reading + moderating the queue
        // (approve/reject/skip/reorder) is Moderator work; changing the config (enable/cost/caps) is Editor.
        M("media:read", Mod);
        M("media:moderate", Mod);
        M("media:write", Editor);

        // Supporter events (supporter-events.md §5) — monetization ingest (tips/memberships/merch/charity).
        // Reading connections + recorded events is Moderator work; connecting a payout/identity-bearing money
        // source is Broadcaster-only, Critical, and NOT permit-delegable.
        M("supporters:read", Mod);
        M("supporters:config:write", Broadcaster, DangerTier.Critical, grant: false);

        // Sound clips (sound-system.md §5) — audio clip library for pipeline SendSound actions. Read
        // (including preview playback, non-mutating) DEFAULTS to Moderator, floor Vip (broadcaster may open it
        // to a VIP); write (upload/update/delete) at Editor.
        MFloor("sounds:read", Mod, Vip);
        M("sounds:write", Editor);

        // ── Community plane (Default = Floor = Everyone(0), Tier = Low, Grant = true) ──
        void C(string key) =>
            s.Add(
                new ActionSeed(key, Everyone, Everyone, DangerTier.Low, true, AuthPlane.Community)
            );

        C("music:request:submit");
        C("moderation:report:file");
        C("economy:catalog:read");
        C("economy:catalog:purchase");
        C("economy:games:read");
        C("economy:games:play");
        C("economy:games:history:read");
        C("economy:jars:read");
        C("economy:jars:create");
        C("economy:jars:membership:accept");
        C("economy:jars:membership:revoke");
        C("economy:jars:invite");
        C("economy:jars:contribute");
        C("economy:jars:withdraw");
        C("economy:jars:history:read");
        C("economy:leaderboards:read");
        C("economy:leaderboards:opt-in");
        C("economy:leaderboards:opt-out");
        // Reading ANOTHER member's wallet is a Moderator action (community plane, "self-or-Gate-2" per economy.md
        // §5): a participant reads their OWN wallet via the self-bound GET /accounts/me (Everyone), so the keyed
        // GET /accounts/{viewerUserId} route floors at Moderator. Seeding it at Everyone leaked every viewer's
        // balance to every other viewer.
        s.Add(
            new ActionSeed(
                "economy:account:read",
                Mod,
                Mod,
                DangerTier.Low,
                true,
                AuthPlane.Community
            )
        );
        C("economy:consent:read");
        C("economy:consent:write");
        C("economy:consent:revoke");
        C("economy:transfer:write");
        C("economy:earning");

        // Pronouns (pronouns.md §5) — a viewer setting their OWN pronoun/override. The special-category
        // consent gate is enforced in the service layer, not the role floor.
        C("pronouns:self:write");

        return s;
    }

    private readonly record struct ActionSeed(
        string Key,
        int DefaultLevel,
        int FloorLevel,
        DangerTier Tier,
        bool Grant,
        AuthPlane Plane
    );
}
