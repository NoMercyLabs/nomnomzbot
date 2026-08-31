// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// S-CONSEQ-c2: proves the four delete previews count REAL rows. Each test seeds the referencing rows,
/// including a row owned by ANOTHER channel that must never land in this channel's count, and asserts the
/// exact number. A failed lookup (the resource does not exist here) is asserted to be a FAILURE, never a
/// zero — reporting zero for a check that did not run is the loss the preview exists to prevent.
/// </summary>
public class DeleteBlastRadiusTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherChannel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── Seeding helpers ──────────────────────────────────────────────────────

    /// <summary>Both channels exist in every test — a blast radius is only meaningful inside a real tenant.</summary>
    private static void SeedChannels(BlastRadiusTestDbContext db)
    {
        db.Channels.AddRange(NewChannel(Channel, "streamer"), NewChannel(OtherChannel, "someone"));
    }

    private static NomNomzBot.Domain.Identity.Entities.Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.CreateVersion7(),
            TwitchChannelId = id.ToString(),
            Name = name,
            NameNormalized = name,
            OverlayToken = name + "-token",
        };

    private static Pipeline NewPipeline(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static PipelineStep NewStep(
        Guid broadcaster,
        Guid pipelineId,
        string actionType,
        string configJson
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            PipelineId = pipelineId,
            ActionType = actionType,
            ConfigJson = configJson,
            Order = 0,
        };

    private static SoundClip NewClip(Guid broadcaster, Guid id, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Name = name,
            DisplayName = name,
            StorageKey = $"clips/{id}",
            MimeType = "audio/mpeg",
            CreatedByUserId = Guid.CreateVersion7(),
        };

    private static SoundClipService NewSoundClips(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ISoundClipStore>(),
            Substitute.For<ISoundClipOverlayNotifier>(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IResourceQuotaService>(),
            new PipelineStepReferenceScanner(db)
        );

    private static WidgetService NewWidgets(BlastRadiusTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            Substitute.For<IWidgetBuildService>(),
            Substitute.For<IWidgetSettingsSchemaProvider>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)),
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    private static RewardService NewRewards(BlastRadiusTestDbContext db) =>
        new(db, Substitute.For<ITwitchChannelPointsApi>(), NullLogger<RewardService>.Instance);

    private static GiveawayCodePoolService NewPools(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ITokenProtector>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero))
        );

    // ── Sound clips: the ConfigJson scan ─────────────────────────────────────

    [Fact]
    public async Task Sound_clip_preview_counts_steps_that_name_the_clip_by_id_or_by_name_in_this_channel_only()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid clipId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline alerts = NewPipeline(Channel, "Alerts");
            Pipeline raids = NewPipeline(Channel, "Raids");
            Pipeline foreign = NewPipeline(OtherChannel, "Someone else's pipeline");
            seed.Pipelines.AddRange(alerts, raids, foreign);
            seed.SoundClips.Add(NewClip(Channel, clipId, "airhorn"));

            seed.PipelineSteps.AddRange(
                // Referenced by id.
                NewStep(Channel, alerts.Id, "play_sound", $$"""{"clip":"{{clipId}}"}"""),
                // Referenced by name slug — play_sound resolves either.
                NewStep(Channel, raids.Id, "play_sound", """{"clip":"airhorn"}"""),
                // A different clip: must not count.
                NewStep(Channel, raids.Id, "play_sound", """{"clip":"applause"}"""),
                // A step in ANOTHER channel naming the very same id: must not count.
                NewStep(OtherChannel, foreign.Id, "play_sound", $$"""{"clip":"{{clipId}}"}""")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewSoundClips(db)
            .GetDeleteBlastRadiusAsync(Channel, clipId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalReferences);
        BlastRadiusCategoryDto steps = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.PipelineSteps, steps.CategoryKey);
        Assert.Equal(2, steps.Count);
        Assert.Equal(["Alerts", "Raids"], steps.Sample);
        // Nothing dynamic in this channel — the count is exhaustive, so it is NOT flagged as a floor.
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Sound_clip_preview_is_a_minimum_when_a_step_resolves_the_clip_from_a_template()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid clipId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline alerts = NewPipeline(Channel, "Alerts");
            seed.Pipelines.Add(alerts);
            seed.SoundClips.Add(NewClip(Channel, clipId, "airhorn"));
            seed.PipelineSteps.AddRange(
                NewStep(Channel, alerts.Id, "play_sound", """{"clip":"airhorn"}"""),
                NewStep(Channel, alerts.Id, "play_sound", """{"clip":"{{args.1}}"}""")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewSoundClips(db)
            .GetDeleteBlastRadiusAsync(Channel, clipId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalReferences);
        Assert.True(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Sound_clip_preview_is_a_minimum_when_the_channel_runs_custom_code()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid clipId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline scripted = NewPipeline(Channel, "Scripted");
            seed.Pipelines.Add(scripted);
            seed.SoundClips.Add(NewClip(Channel, clipId, "airhorn"));
            seed.PipelineSteps.Add(NewStep(Channel, scripted.Id, "run_code", "{}"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewSoundClips(db)
            .GetDeleteBlastRadiusAsync(Channel, clipId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A code script can reach the clip through the SDK, so zero matches is a FLOOR, not a clean zero.
        Assert.Equal(0, result.Value.TotalReferences);
        Assert.True(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Sound_clip_preview_reports_a_genuine_zero_when_nothing_references_the_clip()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid clipId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline alerts = NewPipeline(Channel, "Alerts");
            seed.Pipelines.Add(alerts);
            seed.SoundClips.Add(NewClip(Channel, clipId, "airhorn"));
            seed.PipelineSteps.Add(
                NewStep(Channel, alerts.Id, "send_message", """{"message":"hi"}""")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewSoundClips(db)
            .GetDeleteBlastRadiusAsync(Channel, clipId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Categories);
        Assert.Equal(0, result.Value.TotalReferences);
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Sound_clip_preview_fails_for_a_clip_in_another_channel_rather_than_reporting_zero()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid clipId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.SoundClips.Add(NewClip(OtherChannel, clipId, "airhorn"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewSoundClips(db)
            .GetDeleteBlastRadiusAsync(Channel, clipId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    // ── Widgets: a real FK plus the ConfigJson scan ──────────────────────────

    [Fact]
    public async Task Widget_preview_counts_its_versions_and_the_steps_that_name_it_in_this_channel_only()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid widgetId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline alerts = NewPipeline(Channel, "Alerts");
            Pipeline foreign = NewPipeline(OtherChannel, "Foreign");
            seed.Pipelines.AddRange(alerts, foreign);
            seed.Widgets.Add(
                new Widget
                {
                    Id = widgetId,
                    BroadcasterId = Channel,
                    Name = "Alert box",
                }
            );
            seed.WidgetVersions.AddRange(
                new WidgetVersion
                {
                    WidgetId = widgetId,
                    BroadcasterId = Channel,
                    VersionNumber = 1,
                },
                new WidgetVersion
                {
                    WidgetId = widgetId,
                    BroadcasterId = Channel,
                    VersionNumber = 2,
                },
                // Another channel's version row carrying the same widget id must never be counted here.
                new WidgetVersion
                {
                    WidgetId = widgetId,
                    BroadcasterId = OtherChannel,
                    VersionNumber = 1,
                }
            );
            seed.PipelineSteps.AddRange(
                NewStep(Channel, alerts.Id, "widget_event", $$"""{"widget_id":"{{widgetId}}"}"""),
                NewStep(
                    OtherChannel,
                    foreign.Id,
                    "widget_event",
                    $$"""{"widget_id":"{{widgetId}}"}"""
                )
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewWidgets(db)
            .GetDeleteBlastRadiusAsync(
                Channel.ToString(),
                widgetId.ToString(),
                CancellationToken.None
            );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalReferences);
        Assert.Equal(
            [BlastRadiusCategoryKeys.WidgetVersions, BlastRadiusCategoryKeys.PipelineSteps],
            result.Value.Categories.Select(category => category.CategoryKey)
        );
        Assert.Equal(2, result.Value.Categories[0].Count);
        Assert.Equal(1, result.Value.Categories[1].Count);
        Assert.Equal(["Alerts"], result.Value.Categories[1].Sample);
        Assert.False(result.Value.IsMinimum);
    }

    // ── Rewards: real FKs, keyed on Twitch's reward id ───────────────────────

    [Fact]
    public async Task Reward_preview_counts_redemptions_and_timers_carrying_its_twitch_reward_id()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid rewardId = Guid.CreateVersion7();
        const string TwitchRewardId = "twitch-reward-1";

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.Rewards.Add(
                new Reward
                {
                    Id = rewardId,
                    BroadcasterId = Channel,
                    Title = "Hydrate",
                    TwitchRewardId = TwitchRewardId,
                }
            );
            seed.Redemptions.AddRange(
                NewRedemption(Channel, TwitchRewardId, "aaa", "Ana"),
                NewRedemption(Channel, TwitchRewardId, "bbb", "Bo"),
                // Another reward — must not count.
                NewRedemption(Channel, "twitch-reward-2", "ccc", "Cy"),
                // Another channel, same Twitch reward id — must not count.
                NewRedemption(OtherChannel, TwitchRewardId, "ddd", "Di")
            );
            seed.RedemptionTimers.Add(
                new RedemptionTimer
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = Channel,
                    RedemptionId = "aaa",
                    RewardId = TwitchRewardId,
                    RewardTitle = "Hydrate",
                    RedeemedByDisplayName = "Ana",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewRewards(db)
            .GetDeleteBlastRadiusAsync(
                Channel.ToString(),
                rewardId.ToString(),
                CancellationToken.None
            );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalReferences);
        Assert.Equal(BlastRadiusCategoryKeys.Redemptions, result.Value.Categories[0].CategoryKey);
        Assert.Equal(2, result.Value.Categories[0].Count);
        Assert.Equal(["Ana", "Bo"], result.Value.Categories[0].Sample);
        Assert.Equal(
            BlastRadiusCategoryKeys.RedemptionTimers,
            result.Value.Categories[1].CategoryKey
        );
        Assert.Equal(1, result.Value.Categories[1].Count);
        // Every dependent follows a real FK — the total is exhaustive, never a floor.
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Reward_preview_is_a_verified_zero_when_the_reward_was_never_synced_to_twitch()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid rewardId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.Rewards.Add(
                new Reward
                {
                    Id = rewardId,
                    BroadcasterId = Channel,
                    Title = "Draft reward",
                    TwitchRewardId = null,
                }
            );
            // Redemptions exist in the channel, but they belong to OTHER rewards.
            seed.Redemptions.Add(NewRedemption(Channel, "twitch-reward-9", "zzz", "Zed"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewRewards(db)
            .GetDeleteBlastRadiusAsync(
                Channel.ToString(),
                rewardId.ToString(),
                CancellationToken.None
            );

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Categories);
        Assert.Equal(0, result.Value.TotalReferences);
        Assert.False(result.Value.IsMinimum);
    }

    private static Redemption NewRedemption(
        Guid broadcaster,
        string twitchRewardId,
        string redemptionId,
        string displayName
    ) =>
        new()
        {
            BroadcasterId = broadcaster,
            RedemptionId = redemptionId,
            RewardId = twitchRewardId,
            RewardTitle = "Hydrate",
            UserId = displayName.ToLowerInvariant(),
            UserDisplayName = displayName,
            Status = "unfulfilled",
        };

    // ── Giveaway code pools: real FKs ────────────────────────────────────────

    [Fact]
    public async Task Code_pool_preview_counts_its_codes_and_giveaways_in_this_channel_only()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid poolId = Guid.CreateVersion7();
        Guid otherPoolId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.GiveawayCodePools.AddRange(
                new GiveawayCodePool
                {
                    Id = poolId,
                    BroadcasterId = Channel,
                    Name = "Steam keys",
                },
                new GiveawayCodePool
                {
                    Id = otherPoolId,
                    BroadcasterId = Channel,
                    Name = "Other keys",
                }
            );
            seed.GiveawayCodes.AddRange(
                NewCode(Channel, poolId),
                NewCode(Channel, poolId),
                NewCode(Channel, poolId),
                // Another pool in the same channel — must not count.
                NewCode(Channel, otherPoolId),
                // Same pool id, another channel — must not count.
                NewCode(OtherChannel, poolId)
            );
            seed.Giveaways.AddRange(
                NewGiveaway(Channel, poolId, "Summer drop"),
                NewGiveaway(OtherChannel, poolId, "Foreign drop")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewPools(db)
            .GetDeleteBlastRadiusAsync(Channel, poolId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalReferences);
        Assert.Equal(BlastRadiusCategoryKeys.GiveawayCodes, result.Value.Categories[0].CategoryKey);
        Assert.Equal(3, result.Value.Categories[0].Count);
        Assert.Equal(BlastRadiusCategoryKeys.Giveaways, result.Value.Categories[1].CategoryKey);
        Assert.Equal(1, result.Value.Categories[1].Count);
        Assert.Equal(["Summer drop"], result.Value.Categories[1].Sample);
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Code_pool_preview_fails_for_a_pool_in_another_channel_rather_than_reporting_zero()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid poolId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.GiveawayCodePools.Add(
                new GiveawayCodePool
                {
                    Id = poolId,
                    BroadcasterId = OtherChannel,
                    Name = "Steam keys",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewPools(db)
            .GetDeleteBlastRadiusAsync(Channel, poolId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    private static GiveawayCode NewCode(Guid broadcaster, Guid poolId) =>
        new()
        {
            BroadcasterId = broadcaster,
            CodePoolId = poolId,
            CodeCipher = "cipher",
        };

    private static Giveaway NewGiveaway(Guid broadcaster, Guid poolId, string title) =>
        new()
        {
            BroadcasterId = broadcaster,
            Title = title,
            EntryMode = "keyword",
            PrizeMode = "code",
            PrizeCodePoolId = poolId,
        };
}
