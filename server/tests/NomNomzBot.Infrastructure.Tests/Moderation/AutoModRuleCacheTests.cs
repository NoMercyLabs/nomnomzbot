// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The rule cache has to do two things that pull against each other: survive between chat messages,
/// and stop being trusted the instant an operator edits a rule.
///
/// <para>It did neither. Living on the scoped <c>AutoModerationHandler</c> it was rebuilt per message,
/// so it never served a hit — a database round-trip on every line of chat. And nothing invalidated it,
/// so had it ever worked, a rule change would have taken up to five minutes to bite with nothing on
/// screen saying why the toggle appeared to do nothing.</para>
/// </summary>
public sealed class AutoModRuleCacheTests
{
    private static readonly Guid Channel = Guid.Parse("0192d100-0000-7000-8000-0000000000b1");
    private static readonly Guid OtherChannel = Guid.Parse("0192d100-0000-7000-8000-0000000000b2");

    private static async Task<(
        AutoModRuleCache Cache,
        ModerationServiceTestDbContext Db
    )> BuildAsync(string ruleName = "no links")
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Records.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = Channel.ToString(),
                RecordType = "moderation_rule",
                Data = $$"""
                { "Name": "{{ruleName}}", "Type": "links", "Action": "timeout", "IsEnabled": true }
                """,
            }
        );
        await db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        AutoModRuleCache cache = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AutoModRuleCache>.Instance
        );
        return (cache, db);
    }

    private static async Task RenameRuleAsync(ModerationServiceTestDbContext db, string newName)
    {
        Domain.Platform.Entities.Record record = db.Records.Single();
        record.Data = $$"""
            { "Name": "{{newName}}", "Type": "links", "Action": "timeout", "IsEnabled": true }
            """;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ASecondReadIsServedFromTheCacheRatherThanTheDatabase()
    {
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();

        IReadOnlyList<AutoModRule> first = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("no links", Assert.Single(first).Name);

        // Change the row underneath the cache. A cache that is actually caching cannot see this;
        // one that re-reads per call would, which is exactly the per-message round-trip being fixed.
        await RenameRuleAsync(db, "changed underneath");

        IReadOnlyList<AutoModRule> second = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("no links", Assert.Single(second).Name);
    }

    [Fact]
    public async Task InvalidatingMakesTheNextReadSeeTheNewRules()
    {
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();

        await cache.GetAsync(Channel, CancellationToken.None);
        await RenameRuleAsync(db, "operator edited this");
        cache.Invalidate(Channel);

        IReadOnlyList<AutoModRule> rules = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("operator edited this", Assert.Single(rules).Name);
    }

    [Fact]
    public async Task InvalidatingOneChannelLeavesTheOthersAlone()
    {
        // A busy instance runs many channels; evicting all of them on one operator's edit would turn
        // every rule change into an instance-wide reload storm.
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();

        await cache.GetAsync(Channel, CancellationToken.None);
        await RenameRuleAsync(db, "changed underneath");
        cache.Invalidate(OtherChannel);

        IReadOnlyList<AutoModRule> rules = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("no links", Assert.Single(rules).Name);
    }

    [Theory]
    [InlineData("moderation-rules")]
    [InlineData("automod")]
    public async Task TheInvalidatorEvictsForEveryDomainThatWritesRules(string domain)
    {
        // Both dashboard surfaces write moderation_rule records. A domain missing from the set is a
        // rule change that silently does not take effect.
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();
        AutoModRuleCacheInvalidator invalidator = new(cache);

        await cache.GetAsync(Channel, CancellationToken.None);
        await RenameRuleAsync(db, "operator edited this");

        await invalidator.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                Domain = domain,
                Action = "updated",
            },
            CancellationToken.None
        );

        IReadOnlyList<AutoModRule> rules = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("operator edited this", Assert.Single(rules).Name);
    }

    [Fact]
    public async Task AnUnrelatedConfigChangeDoesNotEvict()
    {
        // Every config write in the product raises this event. Evicting on all of them would make the
        // cache useless on a channel where anyone is editing anything.
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();
        AutoModRuleCacheInvalidator invalidator = new(cache);

        await cache.GetAsync(Channel, CancellationToken.None);
        await RenameRuleAsync(db, "changed underneath");

        await invalidator.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                Domain = "tts-config",
                Action = "updated",
            },
            CancellationToken.None
        );

        IReadOnlyList<AutoModRule> rules = await cache.GetAsync(Channel, CancellationToken.None);
        Assert.Equal("no links", Assert.Single(rules).Name);
    }

    [Fact]
    public async Task AMalformedRuleDoesNotDisarmTheRestOfTheChannel()
    {
        (AutoModRuleCache cache, ModerationServiceTestDbContext db) = await BuildAsync();
        db.Records.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = Channel.ToString(),
                RecordType = "moderation_rule",
                Data = "{ this is not json",
            }
        );
        await db.SaveChangesAsync();

        IReadOnlyList<AutoModRule> rules = await cache.GetAsync(Channel, CancellationToken.None);

        Assert.Equal("no links", Assert.Single(rules).Name);
    }
}
