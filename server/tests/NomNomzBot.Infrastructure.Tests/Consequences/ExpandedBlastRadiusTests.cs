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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Assets;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.PickLists;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// S-CONSEQ-c3: proves the eleven previews added in this slice count REAL rows from REAL SQL. Every counted
/// test seeds a referencing row owned by ANOTHER channel that must never appear in this channel's number, and
/// asserts the exact count and the sample names. A resource that does not exist in this tenant is asserted to
/// be a FAILURE, not a zero — the dashboard renders a failed lookup as its own "could not check" message, and
/// a zero standing in for an unrun check is exactly the loss these previews exist to prevent.
/// </summary>
public class ExpandedBlastRadiusTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherChannel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero)
    );

    // ── Seeding helpers ──────────────────────────────────────────────────────

    private static void SeedChannels(BlastRadiusTestDbContext db) =>
        db.Channels.AddRange(NewChannel(Channel, "streamer"), NewChannel(OtherChannel, "someone"));

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
        string configJson,
        Guid? codeScriptId = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            PipelineId = pipelineId,
            ActionType = actionType,
            ConfigJson = configJson,
            CodeScriptId = codeScriptId,
            Order = 0,
        };

    // ── Pick lists: the ConfigJson scan on the `list` field ───────────────────

    [Fact]
    public async Task Pick_list_preview_counts_the_steps_that_name_it_and_never_another_channels_step()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid listId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline greetings = NewPipeline(Channel, "Greetings");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(greetings, foreign);
            seed.PickLists.Add(
                new()
                {
                    Id = listId,
                    BroadcasterId = Channel,
                    Name = "compliments",
                }
            );
            seed.PipelineSteps.AddRange(
                NewStep(Channel, greetings.Id, "pick_from_list", """{"list":"compliments"}"""),
                NewStep(Channel, greetings.Id, "pick_from_list", $$"""{"list":"{{listId}}"}"""),
                NewStep(Channel, greetings.Id, "pick_from_list", """{"list":"insults"}"""),
                NewStep(OtherChannel, foreign.Id, "pick_from_list", """{"list":"compliments"}""")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewPickLists(db)
            .GetDeleteBlastRadiusAsync(Channel, listId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto steps = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.PipelineSteps, steps.CategoryKey);
        Assert.Equal(2, steps.Count);
        Assert.Equal(["Greetings"], steps.Sample);
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Pick_list_preview_fails_rather_than_reporting_zero_for_another_channels_list()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid listId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.PickLists.Add(
                new()
                {
                    Id = listId,
                    BroadcasterId = OtherChannel,
                    Name = "compliments",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewPickLists(db)
            .GetDeleteBlastRadiusAsync(Channel, listId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    private static PickListService NewPickLists(BlastRadiusTestDbContext db) =>
        new(db, Substitute.For<IEventBus>(), new PipelineStepReferenceScanner(db));

    // ── Code scripts: two real foreign keys ──────────────────────────────────

    [Fact]
    public async Task Code_script_preview_counts_its_saved_versions_and_the_steps_that_run_it()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid scriptId = Guid.CreateVersion7();
        Guid otherScriptId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            Pipeline chat = NewPipeline(Channel, "Chat");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(chat, foreign);
            seed.CodeScripts.AddRange(
                NewScript(Channel, scriptId, "shoutout"),
                NewScript(Channel, otherScriptId, "unrelated")
            );
            seed.CodeScriptVersions.AddRange(
                NewScriptVersion(Channel, scriptId, 1),
                NewScriptVersion(Channel, scriptId, 2),
                NewScriptVersion(Channel, otherScriptId, 1)
            );
            seed.PipelineSteps.AddRange(
                NewStep(Channel, chat.Id, "run_code", "{}", scriptId),
                NewStep(Channel, chat.Id, "run_code", "{}", otherScriptId),
                NewStep(OtherChannel, foreign.Id, "run_code", "{}", scriptId)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewCodeScripts(db, Channel)
            .GetDeleteBlastRadiusAsync(scriptId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalReferences);
        BlastRadiusCategoryDto steps = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.PipelineSteps
        );
        Assert.Equal(1, steps.Count);
        Assert.Equal(["Chat"], steps.Sample);
        BlastRadiusCategoryDto versions = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.CodeScriptVersions
        );
        Assert.Equal(2, versions.Count);
        // Both dependents carry a real FK, so this total is exhaustive — never rendered as a floor.
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Code_script_preview_fails_rather_than_reporting_zero_for_another_channels_script()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid scriptId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.CodeScripts.Add(NewScript(OtherChannel, scriptId, "theirs"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewCodeScripts(db, Channel)
            .GetDeleteBlastRadiusAsync(scriptId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    private static CodeScript NewScript(Guid broadcaster, Guid id, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static CodeScriptVersion NewScriptVersion(
        Guid broadcaster,
        Guid scriptId,
        int version
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            CodeScriptId = scriptId,
            Version = version,
            SourceCode = "export default () => {};",
        };

    private static CodeScriptService NewCodeScripts(BlastRadiusTestDbContext db, Guid broadcaster)
    {
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(broadcaster);
        return new(
            db,
            tenant,
            Substitute.For<IScriptExecutor>(),
            Substitute.For<IEventBus>(),
            Clock,
            Substitute.For<IWidgetDependencyAllowlist>()
        );
    }

    // ── Assets: the serving-path search, always a floor ──────────────────────

    [Fact]
    public async Task Asset_preview_counts_the_widgets_and_steps_that_embed_its_serving_path_as_a_minimum()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid assetId = Guid.CreateVersion7();
        string path = $"assets/file/{Channel}/logo";

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.ChannelAssets.Add(NewAsset(Channel, assetId, "logo"));
            // Another channel's asset with the SAME name: its path differs, so it never collides.
            seed.ChannelAssets.Add(NewAsset(OtherChannel, Guid.CreateVersion7(), "logo"));

            Pipeline alerts = NewPipeline(Channel, "Alerts");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(alerts, foreign);
            seed.PipelineSteps.AddRange(
                NewStep(
                    Channel,
                    alerts.Id,
                    "widget_event",
                    $$"""{"image":"/api/v1/{{path}}?v=2"}"""
                ),
                NewStep(
                    Channel,
                    alerts.Id,
                    "widget_event",
                    """{"image":"/api/v1/assets/file/x/other"}"""
                ),
                NewStep(
                    OtherChannel,
                    foreign.Id,
                    "widget_event",
                    $$"""{"image":"/api/v1/{{path}}"}"""
                )
            );

            Widget overlay = NewWidget(Channel, "Overlay");
            Widget unrelated = NewWidget(Channel, "Chat box");
            Widget foreignWidget = NewWidget(OtherChannel, "Theirs");
            seed.Widgets.AddRange(overlay, unrelated, foreignWidget);
            seed.WidgetVersions.AddRange(
                NewWidgetVersion(Channel, overlay.Id, $"<img src=\"/api/v1/{path}\">"),
                NewWidgetVersion(Channel, unrelated.Id, "<div/>"),
                NewWidgetVersion(OtherChannel, foreignWidget.Id, $"<img src=\"/api/v1/{path}\">")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewAssets(db)
            .GetDeleteBlastRadiusAsync(Channel, assetId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto steps = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.PipelineSteps
        );
        Assert.Equal(1, steps.Count);
        Assert.Equal(["Alerts"], steps.Sample);
        BlastRadiusCategoryDto widgets = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.Widgets
        );
        Assert.Equal(1, widgets.Count);
        Assert.Equal(["Overlay"], widgets.Sample);
        // A URL can be built at run time, so the number is a FLOOR and the dialog must say MINIMUM.
        Assert.True(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Asset_preview_with_no_reference_is_still_a_minimum_so_a_floor_never_reads_as_verified_nothing()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid assetId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.ChannelAssets.Add(NewAsset(Channel, assetId, "logo"));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewAssets(db)
            .GetDeleteBlastRadiusAsync(Channel, assetId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Categories);
        Assert.Equal(0, result.Value.TotalReferences);
        Assert.True(result.Value.IsMinimum);
    }

    private static ChannelAsset NewAsset(Guid broadcaster, Guid id, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Name = name,
            DisplayName = name,
            Kind = "image",
            MimeType = "image/png",
            StorageKey = $"assets/{id}",
            CreatedByUserId = Guid.CreateVersion7(),
        };

    private static Widget NewWidget(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static WidgetVersion NewWidgetVersion(Guid broadcaster, Guid widgetId, string source) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            WidgetId = widgetId,
            VersionNumber = 1,
            SourceCode = source,
        };

    private static ChannelAssetService NewAssets(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<IChannelAssetStore>(),
            Substitute.For<IResourceQuotaService>(),
            new PipelineStepReferenceScanner(db)
        );

    // ── Custom data sources: the event type they fire ────────────────────────

    [Fact]
    public async Task Custom_data_source_preview_counts_the_event_responses_bound_to_its_event_type()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid sourceId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.CustomDataSources.Add(NewSource(Channel, sourceId, "weather"));
            seed.EventResponses.AddRange(
                NewEventResponse(Channel, "custom.weather"),
                NewEventResponse(Channel, "custom.stocks"),
                // Same event type in ANOTHER channel: must not count.
                NewEventResponse(OtherChannel, "custom.weather")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewDataSources(db)
            .GetDeleteBlastRadiusAsync(Channel, sourceId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto responses = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.EventResponses, responses.CategoryKey);
        Assert.Equal(1, responses.Count);
        Assert.Equal(["custom.weather"], responses.Sample);
        // Templates and code scripts can name the source invisibly — an honest floor.
        Assert.True(result.Value.IsMinimum);
    }

    private static CustomDataSource NewSource(Guid broadcaster, Guid id, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Name = name,
            DisplayName = name,
            SourceKind = "push",
            CreatedByUserId = Guid.CreateVersion7(),
        };

    private static EventResponse NewEventResponse(Guid broadcaster, string eventType) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            EventType = eventType,
        };

    private static CustomDataSourceService NewDataSources(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ICustomDataIngestService>(),
            Substitute.For<ICustomDataEgressFetcher>(),
            []
        );

    // ── Catalog items and leaderboard configs: plain FK counts ───────────────

    [Fact]
    public async Task Catalog_item_preview_counts_the_purchases_of_this_channel_only()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid itemId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.CatalogItems.Add(
                new()
                {
                    Id = itemId,
                    BroadcasterId = Channel,
                    Name = "Emote slot",
                    NameNormalized = "emote slot",
                    SinkType = "manual",
                    Cost = 100,
                }
            );
            seed.CatalogPurchases.AddRange(
                NewPurchase(Channel, itemId),
                NewPurchase(Channel, itemId),
                NewPurchase(OtherChannel, itemId)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewCatalog(db)
            .GetDeleteItemBlastRadiusAsync(Channel, itemId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto purchases = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.CatalogPurchases, purchases.CategoryKey);
        Assert.Equal(2, purchases.Count);
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Catalog_item_preview_reports_an_explicit_verified_zero_when_nothing_was_ever_bought()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid itemId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.CatalogItems.Add(
                new()
                {
                    Id = itemId,
                    BroadcasterId = Channel,
                    Name = "Emote slot",
                    NameNormalized = "emote slot",
                    SinkType = "manual",
                    Cost = 100,
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewCatalog(db)
            .GetDeleteItemBlastRadiusAsync(Channel, itemId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Categories);
        // Every dependent is FK-backed, so this zero is VERIFIED, not a floor.
        Assert.False(result.Value.IsMinimum);
    }

    private static CatalogPurchase NewPurchase(Guid broadcaster, Guid itemId) =>
        new()
        {
            BroadcasterId = broadcaster,
            CatalogItemId = itemId,
            BuyerAccountId = Guid.CreateVersion7(),
            BuyerUserId = Guid.CreateVersion7(),
            CostPaid = 100,
            ItemNameSnapshot = "Emote slot",
        };

    private static CatalogService NewCatalog(BlastRadiusTestDbContext db) =>
        new(db, Substitute.For<ICurrencyAccountService>(), Substitute.For<IEventBus>(), Clock);

    [Fact]
    public async Task Leaderboard_config_preview_counts_the_snapshots_it_would_orphan()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid configId = Guid.CreateVersion7();
        Guid otherConfigId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.LeaderboardConfigs.AddRange(
                NewLeaderboard(Channel, configId),
                NewLeaderboard(OtherChannel, otherConfigId)
            );
            seed.LeaderboardSnapshots.AddRange(
                NewSnapshot(Channel, configId),
                NewSnapshot(Channel, configId),
                NewSnapshot(Channel, otherConfigId),
                NewSnapshot(OtherChannel, configId)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await new EconomyLeaderboardService(
            db,
            Clock
        ).GetDeleteConfigBlastRadiusAsync(Channel, configId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto snapshots = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.LeaderboardSnapshots, snapshots.CategoryKey);
        Assert.Equal(2, snapshots.Count);
        Assert.False(result.Value.IsMinimum);
    }

    private static LeaderboardConfig NewLeaderboard(Guid broadcaster, Guid id) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Metric = "currency",
            Scope = "channel",
            Period = "all_time",
            TopN = 10,
        };

    private static LeaderboardSnapshot NewSnapshot(Guid broadcaster, Guid configId) =>
        new()
        {
            BroadcasterId = broadcaster,
            LeaderboardConfigId = configId,
            PeriodKey = "all_time",
            Rank = 1,
            SubjectTwitchUserId = "123",
            DisplayNameSnapshot = "viewer",
            Value = 1,
        };

    // ── Giveaways: entrants and winners ──────────────────────────────────────

    [Fact]
    public async Task Giveaway_preview_counts_its_entrants_and_winners_of_this_channel_only()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid giveawayId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.Giveaways.Add(
                new()
                {
                    Id = giveawayId,
                    BroadcasterId = Channel,
                    Title = "Steam key",
                    EntryMode = "chat",
                    PrizeMode = "manual",
                }
            );
            seed.GiveawayEntries.AddRange(
                NewEntry(Channel, giveawayId),
                NewEntry(Channel, giveawayId),
                NewEntry(Channel, giveawayId),
                NewEntry(OtherChannel, giveawayId)
            );
            seed.GiveawayWinners.AddRange(
                NewWinner(Channel, giveawayId),
                NewWinner(OtherChannel, giveawayId)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewGiveaways(db)
            .GetDeleteBlastRadiusAsync(Channel, giveawayId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalReferences);
        Assert.Equal(
            3,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.GiveawayEntries
                )
                .Count
        );
        Assert.Equal(
            1,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.GiveawayWinners
                )
                .Count
        );
        Assert.False(result.Value.IsMinimum);
    }

    private static GiveawayEntry NewEntry(Guid broadcaster, Guid giveawayId) =>
        new()
        {
            BroadcasterId = broadcaster,
            GiveawayId = giveawayId,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = Guid.CreateVersion7().ToString(),
        };

    private static GiveawayWinner NewWinner(Guid broadcaster, Guid giveawayId) =>
        new()
        {
            BroadcasterId = broadcaster,
            GiveawayId = giveawayId,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = Guid.CreateVersion7().ToString(),
        };

    private static GiveawayService NewGiveaways(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IEventBus>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IGiveawayFulfillment>(),
            Substitute.For<IAgeConsentService>(),
            Clock,
            NullLogger<GiveawayService>.Instance
        );

    // ── Inbound webhooks: what stops receiving ───────────────────────────────

    [Fact]
    public async Task Inbound_webhook_preview_counts_the_push_sources_and_supporter_feeds_it_carries()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid endpointId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.InboundWebhookEndpoints.Add(NewEndpoint(Channel, endpointId));

            CustomDataSource bound = NewSource(Channel, Guid.CreateVersion7(), "kofi");
            bound.InboundWebhookEndpointId = endpointId;
            CustomDataSource unbound = NewSource(Channel, Guid.CreateVersion7(), "weather");
            CustomDataSource foreign = NewSource(OtherChannel, Guid.CreateVersion7(), "theirs");
            foreign.InboundWebhookEndpointId = endpointId;
            seed.CustomDataSources.AddRange(bound, unbound, foreign);

            seed.SupporterConnections.AddRange(
                NewSupporter(Channel, "kofi", endpointId),
                NewSupporter(OtherChannel, "kofi", endpointId)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewInboundWebhooks(db)
            .GetDeleteBlastRadiusAsync(Channel, endpointId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto sources = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.CustomDataSources
        );
        Assert.Equal(1, sources.Count);
        Assert.Equal(["kofi"], sources.Sample);
        Assert.Equal(
            1,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.SupporterConnections
                )
                .Count
        );
        Assert.False(result.Value.IsMinimum);
    }

    private static InboundWebhookEndpoint NewEndpoint(Guid broadcaster, Guid id) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            Name = "ko-fi",
            Token = id.ToString("N"),
            VerificationSecretEnvelope = "{}",
            EncryptionKeyId = Guid.CreateVersion7(),
        };

    private static SupporterConnection NewSupporter(
        Guid broadcaster,
        string sourceKey,
        Guid? endpointId
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            SourceKey = sourceKey,
            ConnectionMode = "webhook",
            InboundWebhookEndpointId = endpointId,
        };

    private static InboundWebhookEndpointService NewInboundWebhooks(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ISubjectKeyService>(),
            new ConfigurationBuilder().Build(),
            Clock,
            Substitute.For<IEventBus>()
        );

    // ── Discord: what stops working, not what is deleted ─────────────────────

    [Fact]
    public async Task Discord_disconnect_preview_counts_the_rules_role_buttons_and_discord_steps()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid connectionId = Guid.CreateVersion7();
        Guid otherConnectionId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.DiscordGuildConnections.AddRange(
                NewGuild(Channel, connectionId),
                NewGuild(OtherChannel, otherConnectionId)
            );
            seed.DiscordNotificationConfigs.AddRange(
                NewNotificationConfig(Channel, connectionId),
                NewNotificationConfig(Channel, connectionId),
                NewNotificationConfig(OtherChannel, otherConnectionId)
            );
            seed.DiscordNotificationRoles.Add(NewNotificationRole(Channel, connectionId));

            Pipeline live = NewPipeline(Channel, "Going live");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(live, foreign);
            seed.PipelineSteps.AddRange(
                NewStep(Channel, live.Id, "send_discord_notification", "{}"),
                NewStep(Channel, live.Id, "send_message", "{}"),
                NewStep(OtherChannel, foreign.Id, "send_discord_notification", "{}")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewDiscord(db)
            .GetDisconnectBlastRadiusAsync(Channel, connectionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalReferences);
        Assert.Equal(
            2,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.DiscordNotificationRules
                )
                .Count
        );
        Assert.Equal(
            1,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.DiscordRoleButtons
                )
                .Count
        );
        BlastRadiusCategoryDto steps = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.PipelineSteps
        );
        Assert.Equal(1, steps.Count);
        Assert.Equal(["Going live"], steps.Sample);
        Assert.False(result.Value.IsMinimum);
    }

    private static DiscordGuildConnection NewGuild(Guid broadcaster, Guid id) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            GuildId = id.ToString("N"),
        };

    private static DiscordNotificationConfig NewNotificationConfig(
        Guid broadcaster,
        Guid connectionId
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            GuildConnectionId = connectionId,
            TriggerType = "stream_online",
            TargetChannelId = "1",
        };

    private static DiscordNotificationRole NewNotificationRole(
        Guid broadcaster,
        Guid connectionId
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            GuildConnectionId = connectionId,
            DiscordRoleId = "42",
        };

    private static DiscordGuildService NewDiscord(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<IEventBus>(),
            Clock,
            new PipelineStepReferenceScanner(db)
        );

    // ── Integrations: the silent killer ──────────────────────────────────────

    [Fact]
    public async Task Spotify_disconnect_preview_counts_every_music_step_that_would_go_dead()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.IntegrationConnections.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = Channel,
                    Provider = "spotify",
                    Status = "active",
                }
            );

            Pipeline music = NewPipeline(Channel, "Music");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(music, foreign);
            seed.PipelineSteps.AddRange(
                NewStep(Channel, music.Id, "song_skip", "{}"),
                NewStep(Channel, music.Id, "music_set_volume", "{}"),
                NewStep(Channel, music.Id, "send_message", "{}"),
                NewStep(OtherChannel, foreign.Id, "song_skip", "{}")
            );
            seed.SupporterConnections.Add(NewSupporter(Channel, "spotify", null));
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await new IntegrationBlastRadiusService(
            db,
            new PipelineStepReferenceScanner(db)
        ).GetDisconnectBlastRadiusAsync(Channel, "spotify", CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto steps = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.PipelineSteps
        );
        Assert.Equal(2, steps.Count);
        Assert.Equal(["Music"], steps.Sample);
        Assert.Equal(
            1,
            result
                .Value.Categories.Single(c =>
                    c.CategoryKey == BlastRadiusCategoryKeys.SupporterConnections
                )
                .Count
        );
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Integration_disconnect_preview_is_a_minimum_when_the_channel_runs_code_scripts()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.IntegrationConnections.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = Channel,
                    Provider = "spotify",
                    Status = "active",
                }
            );
            Pipeline music = NewPipeline(Channel, "Music");
            seed.Pipelines.Add(music);
            seed.PipelineSteps.AddRange(
                NewStep(Channel, music.Id, "song_skip", "{}"),
                // A code script reaches Spotify through the SDK; that call is invisible to any scan.
                NewStep(Channel, music.Id, "run_code", "{}")
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await new IntegrationBlastRadiusService(
            db,
            new PipelineStepReferenceScanner(db)
        ).GetDisconnectBlastRadiusAsync(Channel, "spotify", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Integration_disconnect_preview_fails_rather_than_reporting_zero_when_nothing_is_connected()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            // The connection belongs to the OTHER channel — this tenant has none.
            seed.IntegrationConnections.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = OtherChannel,
                    Provider = "spotify",
                    Status = "active",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await new IntegrationBlastRadiusService(
            db,
            new PipelineStepReferenceScanner(db)
        ).GetDisconnectBlastRadiusAsync(Channel, "spotify", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    // ── Bundles: WHAT the uninstall removes ──────────────────────────────────

    [Fact]
    public async Task Bundle_uninstall_preview_names_what_it_removes_per_kind_from_the_install_ledger()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid bundleId = Guid.CreateVersion7();
        Pipeline greetings = NewPipeline(Channel, "Greetings");
        Pipeline farewells = NewPipeline(Channel, "Farewells");
        Guid listId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.Pipelines.AddRange(greetings, farewells);
            seed.PickLists.Add(
                new()
                {
                    Id = listId,
                    BroadcasterId = Channel,
                    Name = "compliments",
                }
            );
            seed.InstalledBundles.Add(
                new()
                {
                    Id = bundleId,
                    BroadcasterId = Channel,
                    Name = "Greeter pack",
                    Version = "1.0.0",
                    ManifestJson = "{}",
                    InstalledEntityIdsJson = $$"""
                    {"pipeline":["{{greetings.Id}}","{{farewells.Id}}"],"pick_list":["{{listId}}"]}
                    """,
                    InstalledByUserId = Guid.CreateVersion7(),
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewBundles(db)
            .GetUninstallBlastRadiusAsync(Channel, bundleId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalReferences);
        BlastRadiusCategoryDto pipelines = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.Pipelines
        );
        Assert.Equal(2, pipelines.Count);
        Assert.Equal(["Farewells", "Greetings"], pipelines.Sample);
        BlastRadiusCategoryDto lists = result.Value.Categories.Single(c =>
            c.CategoryKey == BlastRadiusCategoryKeys.PickLists
        );
        Assert.Equal(1, lists.Count);
        Assert.Equal(["compliments"], lists.Sample);
        // Every kind was recognised, so the ledger total is exhaustive.
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Bundle_uninstall_preview_fails_rather_than_showing_an_empty_radius_for_an_unreadable_ledger()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        Guid bundleId = Guid.CreateVersion7();

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            SeedChannels(seed);
            seed.InstalledBundles.Add(
                new()
                {
                    Id = bundleId,
                    BroadcasterId = Channel,
                    Name = "Broken pack",
                    Version = "1.0.0",
                    ManifestJson = "{}",
                    InstalledEntityIdsJson = "not json at all",
                    InstalledByUserId = Guid.CreateVersion7(),
                }
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewBundles(db)
            .GetUninstallBlastRadiusAsync(Channel, bundleId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("LEDGER_UNREADABLE", result.ErrorCode);
    }

    private static BundleImportService NewBundles(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ICommandService>(),
            Substitute.For<IPipelineService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<ISoundClipService>(),
            Substitute.For<IChannelAssetService>(),
            Substitute.For<ICustomDataSourceService>(),
            Substitute.For<IEventResponseService>(),
            Substitute.For<IRewardService>(),
            Substitute.For<ITimerManagementService>(),
            Substitute.For<IChatTriggerService>(),
            Substitute.For<IPickListService>(),
            Substitute.For<ICodeScriptService>(),
            Substitute.For<ICurrentTenantService>(),
            Substitute.For<IEventBus>()
        );
}
