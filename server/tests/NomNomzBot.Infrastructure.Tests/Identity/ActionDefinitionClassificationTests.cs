// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-MOD-PERMS — proves the owner's classification rule for moderator permissions holds for EVERY
/// management-plane action key in the live seeder catalogue, not a hand-picked sample:
/// <list type="number">
/// <item>Category 1 (Twitch-native) — mirrors Twitch's own role rules; not asserted numerically here
/// (that is a per-action Twitch-parity fact), but every such key must be accounted for below.</item>
/// <item>Category 2 (bot-internal tooling: commands, pipelines, event responses, timers, chat triggers,
/// quotes, pick-lists, music/song-request control, sound clips, code scripts, widgets, custom data
/// sources, per-viewer data, engagement, media share, giveaways, dashboard/community reads) — MUST
/// default to Moderator (10) or lower so a moderator is not blocked from the bot tooling that is their
/// day-to-day job on a channel they do not own.</item>
/// <item>Category 3 (money, identity/credentials, privilege, destructive-irreversible, platform admin) —
/// MUST default to Broadcaster (40); a moderator must never spend the streamer's money, take over their
/// accounts, or escalate their own privileges.</item>
/// </list>
/// The three key lists below are asserted, together, to be in EXACT bijection with every management-plane
/// key the seeder produces (<see cref="Every_seeded_management_key_is_classified"/>) — a brand-new
/// <c>[RequireAction]</c> key added to a controller without an entry here fails the catalogue-count guard
/// in <see cref="ActionDefinitionSeederTests"/> first, and once seeded, fails THIS test's bijection check,
/// so it cannot silently ship unclassified.
/// </summary>
public sealed class ActionDefinitionClassificationTests
{
    // ── Category 2 — bot-internal tooling. Every key here MUST default to Moderator(10) or lower. ──
    private static readonly string[] Category2Keys =
    [
        "commands:read",
        "commands:write",
        "commands:builtin:read",
        "commands:builtin:write",
        "pipelines:read",
        "pipelines:write",
        "pipelines:validate",
        "eventresponses:read",
        "eventresponses:write",
        "chattriggers:read",
        "chattriggers:write",
        "chatpolls:read",
        "chatpolls:write",
        "timers:read",
        "timers:write",
        "code:script:author",
        "quotes:read",
        "quotes:write",
        "quotes:delete",
        "picklists:read",
        "picklists:write",
        "picklists:delete",
        "sounds:read",
        "sounds:write",
        "widget:read",
        "widget:write",
        "widget:compile",
        "widget:version:read",
        "widget:rollback",
        "widget:install",
        "tts:config:read",
        "tts:config:write",
        "tts:voice:read",
        "tts:voice:test",
        "tts:uservoice:write",
        "tts:queue:review",
        "music:config:read",
        "music:queue:moderate",
        "music:remote:control",
        "music:library:write",
        "music:control:write",
        "customdata:read",
        "customdata:write",
        "viewerdata:read",
        "viewerdata:write",
        "engagement:read",
        "engagement:write",
        "media:read",
        "media:moderate",
        "media:write",
        "giveaways:read",
        "giveaways:write",
        "feature:read",
        "feature:write",
        "bundles:read",
        "sdk:read",
        "dashboard:read",
        "dashboard:replay",
        "community:read",
        "community:trust:write",
        "integration:read",
        "analytics:read",
        "analytics:viewer:read",
        "economy:config:read",
        "economy:earning-rules:read",
        "economy:accounts:read",
        "economy:catalog:purchases:read",
        "economy:account:freeze",
        "economy:account:adjust",
        "economy:ledger:read",
        "economy:leaderboards:config:read",
        "games:session:read",
        "games:session:start",
        "games:session:cancel",
        "reward:read",
        "reward:redemption:read",
        "reward:redemption:fulfill",
        "reward:redemption:refund",
        "webhooks:inbound:read",
        "webhooks:outbound:read",
        "eventsub:read",
        "eventstore:projection:read",
        "twitch:diagnostics:read",
        "obs:control",
        "vts:config:read",
        "vts:control",
        "discord:connection:read",
        "discord:dispatch:read",
        "chat:read",
        "chat:send",
    ];

    // ── Category 3 — restricted regardless of convenience. Every key here MUST default to Broadcaster(40). ──
    private static readonly string[] Category3Keys =
    [
        // Money / billing
        "billing:read",
        "billing:manage",
        // Identity / credentials
        "discord:connection:write",
        "channelbot:connect",
        "channelbot:read",
        "channelbot:disconnect",
        "music:token:read",
        "music:token:rotate",
        "obs:config:read",
        "obs:config:write",
        "vts:config:write",
        "supporters:config:write",
        // Privilege
        "roles:manage",
        "permit:issue",
        "automation:tokens:write",
        "moderation:moderator:write",
        "moderation:vip",
        // Destructive-irreversible / journal integrity
        "eventstore:journal:read",
        "eventstore:projection:rebuild",
        "eventstore:replay:write",
        "eventstore:replay:republish",
        "eventstore:export",
        "eventstore:import",
        "eventstore:import:legacy",
        "giveaways:codes:write",
        // Owner-only onboarding / platform-admin-adjacent
        "setup:write",
        "obs:control:broadcast",
        "economy:games:write",
    ];

    // ── Category 1 (Twitch-native) + other pre-existing, deliberately-researched rows this slice does not
    //    touch (escalated internal moderation tiers, ambiguous economy/integration config, Twitch channel
    //    settings). Listed here ONLY so the bijection check proves completeness; no numeric rule is
    //    asserted beyond "the key exists and is accounted for" — see the class doc.
    private static readonly string[] OtherKeys =
    [
        "bundles:export",
        "bundles:import",
        "bundles:publish",
        "roles:read",
        "discord:config:read",
        "discord:config:write",
        "discord:role:read",
        "discord:role:write",
        "discord:optin:write",
        "moderation:read",
        "moderation:queue:read",
        "moderation:queue:resolve",
        "moderation:action:read",
        "moderation:timeout",
        "moderation:ban",
        "moderation:unban",
        "moderation:delete_message",
        "moderation:warn",
        "moderation:note:write",
        "moderation:automod:read",
        "moderation:automod:write",
        "moderation:filter:read",
        "moderation:filter:write",
        "moderation:nuke",
        "moderation:nuke:read",
        "moderation:sharedban:read",
        "moderation:sharedban:write",
        "moderation:escalation:read",
        "moderation:escalation:write",
        "moderation:report:read",
        "moderation:report:triage",
        "moderation:evidence:build",
        "moderation:usercontext:read",
        "moderation:chat:settings:read",
        "moderation:chat:settings:write",
        "moderation:shieldmode:read",
        "moderation:shieldmode:write",
        "chat:announce",
        "moderation:shoutout",
        "moderation:chatcolor:write",
        "moderation:unbanrequest:read",
        "moderation:unbanrequest:resolve",
        "moderation:blocklist:write",
        "moderation:suspicioususer:write",
        "eventsub:subscribe",
        "eventsub:unsubscribe",
        "music:config:write",
        "stream:read",
        "stream:preset:write",
        "stream:schedule:write",
        "channel:title:write",
        "channel:game:write",
        "channel:tags:write",
        "channel:ccl:write",
        "channel:language:write",
        "channel:brandedcontent:write",
        "channel:extensions:write",
        "chat:whisper:send",
        "live-ops:polls:read",
        "live-ops:polls:write",
        "live-ops:predictions:read",
        "live-ops:predictions:write",
        "live-ops:raids:write",
        "live-ops:ads:read",
        "live-ops:ads:write",
        "live-ops:schedule:read",
        "live-ops:schedule:write",
        "live-ops:marker:create",
        "live-ops:clips:write",
        "automation:tokens:read",
        "webhooks:inbound:write",
        "webhooks:outbound:write",
        "integration:write",
        "economy:config:write",
        "economy:earning-rules:write",
        "economy:earning-rules:delete",
        "economy:catalog:create",
        "economy:catalog:update",
        "economy:catalog:delete",
        "economy:catalog:refund",
        "economy:leaderboards:config:write",
        "economy:leaderboards:config:delete",
        "federation:optin:read",
        "federation:optin:write",
        "federation:optin:delete",
        "reward:manage",
        "reward:sync",
        "supporters:read",
    ];

    private static async Task<AuthDbContext> SeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await new ActionDefinitionSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Every_seeded_management_key_is_classified()
    {
        AuthDbContext db = await SeededAsync();
        List<string> managementKeys = await db
            .ActionDefinitions.Where(a => a.Plane == AuthPlane.Management)
            .Select(a => a.ActionKey)
            .ToListAsync();

        HashSet<string> classified = new(StringComparer.Ordinal);
        classified.UnionWith(Category2Keys);
        classified.UnionWith(Category3Keys);
        classified.UnionWith(OtherKeys);

        // A management key with no entry in ANY of the three lists — e.g. a newly added [RequireAction]
        // key nobody classified — fails here. This is the guard the owner asked for: sweep the whole
        // surface, not a hand-written sample.
        managementKeys
            .Should()
            .BeEquivalentTo(
                classified,
                "every management-plane action key must be classified into category 1/2/3 (S-MOD-PERMS)"
            );
    }

    [Theory]
    [MemberData(nameof(Category2KeyData))]
    public async Task Category2_key_defaults_to_moderator_or_lower(string key)
    {
        AuthDbContext db = await SeededAsync();
        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a => a.ActionKey == key);
        row.DefaultLevel.Should()
            .BeLessThanOrEqualTo(
                10,
                $"'{key}' is bot-internal tooling — a moderator must not be blocked from it by default"
            );
    }

    [Theory]
    [MemberData(nameof(Category3KeyData))]
    public async Task Category3_key_defaults_to_broadcaster_only(string key)
    {
        AuthDbContext db = await SeededAsync();
        ActionDefinition row = await db.ActionDefinitions.SingleAsync(a => a.ActionKey == key);
        row.DefaultLevel.Should()
            .Be(
                40,
                $"'{key}' touches money, credentials, privilege, or irreversible/admin state — "
                    + "never available to a moderator by default"
            );
    }

    public static IEnumerable<object[]> Category2KeyData() =>
        Category2Keys.Select(k => new object[] { k });

    public static IEnumerable<object[]> Category3KeyData() =>
        Category3Keys.Select(k => new object[] { k });
}
