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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// S-CONSEQ-DELETE-CHANNEL: deleting a channel is the largest blast radius in the product, and the preview is
/// the only thing standing between an operator and it. Every test here seeds a SECOND channel's rows that must
/// never appear in the first channel's count — a preview that leaks another tenant's numbers is worse than no
/// preview, because it is believable.
/// </summary>
public class ChannelDeleteBlastRadiusTests
{
    private static readonly Guid Channel = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherChannel = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    // The categories under test, over exactly the tables this focused relational schema maps. The production
    // map (ChannelBlastRadiusSources.All) covers all 116 and is proved complete by
    // ChannelBlastRadiusSourcesCompletenessTests; this subset proves the GROUPING, the tenant filter and the
    // remainder behave over real SQL.
    private static IReadOnlyList<ChannelBlastRadiusSource> Sources =>
        [
            .. ChannelBlastRadiusSources.All.Where(source =>
                MappedEntities.Contains(source.EntityType)
            ),
        ];

    private static readonly HashSet<Type> MappedEntities =
    [
        typeof(Widget),
        typeof(WidgetVersion),
        typeof(Pipeline),
        typeof(PipelineStep),
        typeof(NomNomzBot.Domain.Sound.Entities.SoundClip),
        typeof(Reward),
        typeof(Redemption),
        typeof(RedemptionTimer),
        typeof(NomNomzBot.Domain.Giveaways.Entities.Giveaway),
        typeof(NomNomzBot.Domain.Giveaways.Entities.GiveawayCodePool),
        typeof(NomNomzBot.Domain.Giveaways.Entities.GiveawayCode),
        typeof(EventSubSubscription),
        typeof(DiscordGuildConnection),
        typeof(OutboundWebhookEndpoint),
        typeof(IntegrationConnection),
    ];

    private static ChannelDeletePreviewService NewService(BlastRadiusTestDbContext db) =>
        new(db, new FakeTimeProvider(Now), Sources);

    // ── Seeding ──────────────────────────────────────────────────────────────

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.CreateVersion7(),
            TwitchChannelId = id.ToString(),
            Name = name,
            NameNormalized = name,
            OverlayToken = name + "-token",
        };

    private static Widget NewWidget(Guid broadcaster, string name, bool enabled) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
            IsEnabled = enabled,
        };

    private static Reward NewReward(Guid broadcaster, string title, string? twitchRewardId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Title = title,
            TwitchRewardId = twitchRewardId,
        };

    private static EventSubSubscription NewSubscription(Guid broadcaster, string eventType) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            EventType = eventType,
            Version = "1",
            Enabled = true,
        };

    private static DiscordGuildConnection NewGuild(Guid broadcaster, string guildName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            GuildId = guildName + "-id",
            GuildName = guildName,
            StreamerEnabled = true,
        };

    private static OutboundWebhookEndpoint NewWebhook(
        Guid broadcaster,
        string name,
        bool enabled
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
            Fqdn = "example.invalid",
            SigningSecretEnvelope = "envelope",
            IsEnabled = enabled,
        };

    private static IntegrationConnection NewConnection(Guid broadcaster, string provider) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Provider = provider,
            Status = "connected",
        };

    private static Pipeline NewPipeline(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static void SeedChannels(BlastRadiusTestDbContext db) =>
        db.Channels.AddRange(NewChannel(Channel, "streamer"), NewChannel(OtherChannel, "someone"));

    private static int CountOf(BlastRadiusDto blastRadius, string categoryKey) =>
        blastRadius.Categories.SingleOrDefault(c => c.CategoryKey == categoryKey)?.Count ?? 0;

    // ── Counted categories ───────────────────────────────────────────────────

    [Fact]
    public async Task Preview_GroupsRowsIntoCuratedCategories_AndIgnoresAnotherTenantsRows()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);

            // Overlays: two widgets here, one belonging to someone else.
            db.Widgets.AddRange(
                NewWidget(Channel, "alerts", enabled: true),
                NewWidget(Channel, "chat", enabled: false),
                NewWidget(OtherChannel, "not-mine", enabled: true)
            );

            // Automations: a reward and a pipeline here, one reward elsewhere.
            db.Rewards.AddRange(
                NewReward(Channel, "Hydrate", "twitch-1"),
                NewReward(OtherChannel, "Elsewhere", "twitch-2")
            );
            db.Pipelines.Add(NewPipeline(Channel, "on-follow"));

            // Integrations: one connection here, one elsewhere.
            db.IntegrationConnections.AddRange(
                NewConnection(Channel, "spotify"),
                NewConnection(OtherChannel, "spotify")
            );

            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Channel.ToString());

        Assert.True(result.IsSuccess);
        BlastRadiusDto blastRadius = result.Value!.BlastRadius;

        Assert.Equal(2, CountOf(blastRadius, BlastRadiusCategoryKeys.ChannelOverlays));
        Assert.Equal(2, CountOf(blastRadius, BlastRadiusCategoryKeys.ChannelAutomations));
        Assert.Equal(1, CountOf(blastRadius, BlastRadiusCategoryKeys.ChannelIntegrations));
        Assert.Equal(5, blastRadius.TotalReferences);
    }

    [Fact]
    public async Task Preview_ReportsAnExhaustiveTotal_NotAMinimum()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);
            db.Widgets.Add(NewWidget(Channel, "alerts", enabled: true));
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Channel.ToString());

        // Every category is an exhaustive `WHERE tenant = @id` over a real column, so the dialog must state a
        // total rather than weakening it to "at least this many".
        Assert.False(result.Value!.BlastRadius.IsMinimum);
    }

    [Fact]
    public async Task Preview_ReportsAGenuineNothing_ForAChannelWithNoData()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);
            // Everything belongs to the OTHER channel: a correct preview for this one is an empty list, and
            // the dialog renders that as a verified "nothing", not as an unknown.
            db.Widgets.Add(NewWidget(OtherChannel, "not-mine", enabled: true));
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Channel.ToString());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.BlastRadius.Categories);
        Assert.Equal(0, result.Value.BlastRadius.TotalReferences);
        Assert.Empty(result.Value.ExternalConsequences);
    }

    [Fact]
    public async Task Preview_FailsForAnUnknownChannel_RatherThanReportingZero()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Guid.NewGuid().ToString());

        Assert.True(result.IsFailure);
        Assert.Equal("CHANNEL_NOT_FOUND", result.ErrorCode);
    }

    // ── External consequences ────────────────────────────────────────────────

    [Fact]
    public async Task Preview_NamesExternalConsequences_CountingOnlyRewardsThatReachedTwitch()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);

            db.Rewards.AddRange(
                NewReward(Channel, "Hydrate", "twitch-1"),
                NewReward(Channel, "Posture", "twitch-2"),
                // Never synced to Twitch — nothing breaks THERE when the channel goes.
                NewReward(Channel, "Draft", null),
                NewReward(OtherChannel, "Elsewhere", "twitch-3")
            );

            db.Widgets.AddRange(
                NewWidget(Channel, "alerts", enabled: true),
                // Disabled: no browser source is rendering it, so nothing goes blank.
                NewWidget(Channel, "retired", enabled: false)
            );

            db.EventSubSubscriptions.AddRange(
                NewSubscription(Channel, "channel.follow"),
                NewSubscription(Channel, "channel.subscribe"),
                NewSubscription(OtherChannel, "channel.follow")
            );

            db.DiscordGuildConnections.Add(NewGuild(Channel, "My Server"));

            db.OutboundWebhookEndpoints.AddRange(
                NewWebhook(Channel, "stats", enabled: true),
                NewWebhook(Channel, "paused", enabled: false)
            );

            db.IntegrationConnections.Add(NewConnection(Channel, "spotify"));

            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Channel.ToString());

        IReadOnlyList<ExternalConsequenceDto> external = result.Value!.ExternalConsequences;

        ExternalConsequenceDto rewards = external.Single(c =>
            c.ConsequenceKey == ExternalConsequenceKeys.TwitchRewards
        );
        Assert.Equal(2, rewards.Count);
        Assert.Equal(["Hydrate", "Posture"], rewards.Sample);

        Assert.Equal(
            1,
            external.Single(c => c.ConsequenceKey == ExternalConsequenceKeys.OverlaySources).Count
        );
        Assert.Equal(
            2,
            external
                .Single(c => c.ConsequenceKey == ExternalConsequenceKeys.EventSubSubscriptions)
                .Count
        );
        Assert.Equal(
            ["My Server"],
            external
                .Single(c => c.ConsequenceKey == ExternalConsequenceKeys.DiscordNotifications)
                .Sample
        );
        Assert.Equal(
            1,
            external.Single(c => c.ConsequenceKey == ExternalConsequenceKeys.OutboundWebhooks).Count
        );
        Assert.Equal(
            1,
            external.Single(c => c.ConsequenceKey == ExternalConsequenceKeys.OAuthConnections).Count
        );
    }

    [Fact]
    public async Task Preview_OmitsAnExternalConsequenceThatAffectsNothing()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);
            // A reward that never reached Twitch is the only row: no Twitch-side consequence exists, so the
            // line must be absent rather than present with a zero.
            db.Rewards.Add(NewReward(Channel, "Draft", null));
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        Result<ChannelDeletePreviewDto> result = await NewService(read)
            .PreviewAsync(Channel.ToString());

        Assert.DoesNotContain(
            result.Value!.ExternalConsequences,
            c => c.ConsequenceKey == ExternalConsequenceKeys.TwitchRewards
        );
        // The reward row itself still dies, and is still counted.
        Assert.Equal(
            1,
            CountOf(result.Value.BlastRadius, BlastRadiusCategoryKeys.ChannelAutomations)
        );
    }

    // ── The restore promise ──────────────────────────────────────────────────

    [Fact]
    public async Task Preview_StatesTheChannelName_TheWindow_AndThePermanentAfterDate()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        await using (BlastRadiusTestDbContext db = database.NewContext())
        {
            SeedChannels(db);
            await db.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext read = database.NewContext();
        ChannelDeletePreviewDto preview = (
            await NewService(read).PreviewAsync(Channel.ToString())
        ).Value!;

        // The dialog makes the operator type this to arm the confirm, so it must be the channel's real name.
        Assert.Equal("streamer", preview.ChannelName);
        Assert.Equal(ChannelDeletionPolicy.RestoreWindowDays, preview.RestoreWindowDays);
        Assert.Equal(
            Now.UtcDateTime.AddDays(ChannelDeletionPolicy.RestoreWindowDays),
            preview.PermanentAfterUtc
        );
    }
}
