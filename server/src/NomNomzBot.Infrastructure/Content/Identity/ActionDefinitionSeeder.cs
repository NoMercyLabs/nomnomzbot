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
/// Convention: management-plane rows have <c>DefaultLevel = FloorLevel</c>, <c>Tier = Low</c>, and
/// <c>Grant = true</c> unless the helper overload says otherwise; community-plane rows are
/// <c>Default = Floor = Everyone(0)</c>, <c>Low</c>, grantable. The one row where default ≠ floor
/// (<c>permit:issue</c>) is added explicitly. These rows are the source for the per-spec §5 controller gates.
/// </para>
/// </summary>
public sealed class ActionDefinitionSeeder : ISeeder
{
    private const int Everyone = 0;

    // Unified-ladder rungs below Moderator (AuthorizationLadder: Subscriber=2, Vip=4, Artist=6). Trivial,
    // non-sensitive reads sit at Vip(4) so a VIP viewer can see channel config/content the streamer curates,
    // while a plain viewer (0) is still denied. Kept in sync with PermissionLevel.Vip.ToLevelValue().
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

        // ── Management plane (Default = Floor, Tier = Low, Grant = true unless noted) ──
        void M(string key, int level, DangerTier tier = DangerTier.Low, bool grant = true) =>
            s.Add(new ActionSeed(key, level, level, tier, grant, AuthPlane.Management));

        // Commands / pipelines / responses / timers
        M("commands:read", Vip);
        M("commands:write", Editor);
        M("commands:builtin:read", Mod);
        M("commands:builtin:write", Editor);
        M("pipelines:read", Vip);
        M("pipelines:write", Editor);
        M("pipelines:validate", Vip);
        M("eventresponses:read", Vip);
        M("eventresponses:write", Editor);
        M("timers:read", Vip);
        M("timers:write", Editor);

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
        M("moderation:report:read", Mod);
        M("moderation:report:triage", LeadModerator);
        M("moderation:evidence:build", Mod);
        M("moderation:usercontext:read", Mod);
        M("moderation:chat:settings:read", Mod);
        M("moderation:chat:settings:write", Mod);
        M("moderation:shieldmode:read", Mod);
        M("moderation:shieldmode:write", LeadModerator);
        M("chat:announce", Mod);
        // Dashboard chat page (frontend-ia.md §Chat: read floor Moderator, manage floor Moderator — "live chat
        // console, send-as-bot"): chat-history read + send-a-message-as-the-bot (REST and DashboardHub).
        M("chat:read", Vip);
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
        M("tts:config:read", Mod);
        M("tts:config:write", Editor);
        M("tts:voice:read", Vip);
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
        // the catalogue's *:config:read convention (cf. economy:config:read) at that floor.
        M("music:config:read", Vip);
        M("music:queue:moderate", Mod);
        M("music:token:read", Editor);
        M("music:token:rotate", Broadcaster, DangerTier.Critical, grant: false);
        M("music:remote:control", Mod);
        M("music:library:write", Editor);

        // Stream / channel / live-ops
        // stream-admin.md §5 floors the stream-info/status/category reads at management entry ("Gate 1 only",
        // i.e. Moderator) with no named key; stream:read carries that floor now that Gate 1 is pure entry.
        M("stream:read", Vip);
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

        // Webhooks / widgets / integrations / dashboard / community / setup / analytics
        M("webhooks:inbound:read", Mod);
        M("webhooks:inbound:write", Editor);
        M("webhooks:outbound:read", Mod);
        M("webhooks:outbound:write", Editor);
        M("widget:read", Vip);
        M("widget:write", Editor);
        M("integration:read", Mod);
        M("integration:write", Editor);
        M("community:read", Mod);
        // Managing a viewer's trust level is a per-viewer moderation-tier community write.
        M("community:trust:write", Mod);
        M("dashboard:read", Vip);
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

        // Rewards
        M("reward:read", Vip);
        M("reward:manage", Broadcaster);
        M("reward:sync", Broadcaster);
        M("reward:redemption:read", Mod);
        M("reward:redemption:fulfill", Mod);
        M("reward:redemption:refund", Mod);

        // Quotes (quotes.md §5) — read at Moderator, write at Moderator (mods curate the quote library).
        M("quotes:read", Vip);
        M("quotes:write", Mod);

        // Custom data sources (custom-events.md §5) — the pipeline-facing external data feeds (HypeRate,
        // Pulsoid, webhooks). Read at Moderator, write (create/update/delete/test) at Editor.
        M("customdata:read", Mod);
        M("customdata:write", Editor);

        // Sound clips (sound-system.md §5) — audio clip library for pipeline SendSound actions. Read
        // (including preview playback, non-mutating) at Moderator, write (upload/update/delete) at Editor.
        M("sounds:read", Vip);
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
