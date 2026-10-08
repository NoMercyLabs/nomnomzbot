// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Domain.Alerts.Entities;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the widget half of an activity replay (S-REPLAY-ENDPOINT): <see cref="RenderedAlertReplayer"/> re-sends a
/// previously captured widget push VERBATIM to whichever widgets subscribe to it now, skips the captured TTS when
/// the response itself is being replayed, and writes nothing. Also proves
/// <c>DashboardController.ReplayActivity</c> reports the replay's real counts and never disguises "nothing to
/// replay" as a success.
/// </summary>
public sealed class DashboardControllerReplayTests
{
    private static DashboardController BuildController(IActivityReplayService replay) =>
        new(
            Substitute.For<Domain.Platform.Interfaces.IChannelRegistry>(),
            Substitute.For<Application.Identity.Services.IChannelService>(),
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ITwitchChannelsApi>(),
            Substitute.For<ITwitchSubscriptionsApi>(),
            replay,
            TimeProvider.System,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<DashboardController>>()
        );

    private static (Guid Channel, string EventId, Widget AlertBox, Widget TtsWidget) SeedCaptures(
        ReplayTestDbContext db
    )
    {
        Guid channel = Guid.CreateVersion7();
        string channelEventId = Guid.CreateVersion7().ToString();
        Widget alertBox = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Alert box",
            IsEnabled = true,
            EventSubscriptions = ["follow"],
        };
        Widget ttsWidget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "TTS audio",
            IsEnabled = true,
            EventSubscriptions = [TtsSpeakBroadcastHandler.WidgetEventType],
        };
        db.Widgets.AddRange(alertBox, ttsWidget);
        db.RenderedAlertCaptures.AddRange(
            new RenderedAlertCapture
            {
                BroadcasterId = channel,
                EventType = "follow",
                Payload = """{"user":"PogChamp42","followedAt":"2026-08-29T12:00:00Z"}""",
                ChannelEventId = channelEventId,
            },
            new RenderedAlertCapture
            {
                BroadcasterId = channel,
                EventType = TtsSpeakBroadcastHandler.WidgetEventType,
                Payload = """{"text":"PogChamp42 followed"}""",
                ChannelEventId = channelEventId,
            }
        );
        db.SaveChanges();
        return (channel, channelEventId, alertBox, ttsWidget);
    }

    [Fact]
    public async Task Resend_repushes_the_exact_captured_payload_to_every_currently_subscribed_widget()
    {
        ReplayTestDbContext db = ReplayTestDbContext.New();
        (Guid channel, string eventId, Widget alertBox, _) = SeedCaptures(db);
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();

        int pushed = await new RenderedAlertReplayer(db, notifier).ResendAsync(
            channel,
            eventId,
            includeTts: true
        );

        pushed.Should().Be(2);
        // The recording fake proves the push actually happened, with the exact recorded payload untouched.
        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                alertBox.Id.ToString(),
                Arg.Is<WidgetEventDto>(dto =>
                    dto.EventType == "follow"
                    && ((JsonElement)dto.Data!).GetProperty("user").GetString() == "PogChamp42"
                    && ((JsonElement)dto.Data!).GetProperty("followedAt").GetString()
                        == "2026-08-29T12:00:00Z"
                ),
                Arg.Any<CancellationToken>()
            );
        // A replay is not a new alert: it is never captured again.
        (await db.RenderedAlertCaptures.CountAsync())
            .Should()
            .Be(2);
    }

    [Fact]
    public async Task A_replayed_alert_reaches_the_widget_with_the_same_field_names_the_live_push_had()
    {
        ReplayTestDbContext db = ReplayTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        string eventId = Guid.CreateVersion7().ToString();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Alert box",
                IsEnabled = true,
                EventSubscriptions = ["follow"],
            }
        );
        db.SaveChanges();
        FollowAlertDto follow = new("u1", "PogChamp42", "pogchamp42", null);
        await WidgetAlertDispatch.RouteAsync(
            db,
            Substitute.For<IWidgetNotifier>(),
            channel,
            "follow",
            follow,
            excludeWidgetId: null,
            eventId,
            CancellationToken.None
        );
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        List<string> replayedKeys = [];
        await notifier.SendWidgetEventAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<WidgetEventDto>(dto =>
                replayedKeys.AddRange(
                    ((JsonElement)dto.Data!).EnumerateObject().Select(property => property.Name)
                )
            ),
            Arg.Any<CancellationToken>()
        );

        await new RenderedAlertReplayer(db, notifier).ResendAsync(
            channel,
            eventId,
            includeTts: true
        );

        // The live push goes out through the hub's camelCase JSON, which is what every widget reads (`e.user`).
        replayedKeys.Should().Contain(["user", "displayName", "login", "userId"]);
        replayedKeys.Should().NotContain("User");
    }

    [Fact]
    public async Task Resend_skips_the_captured_tts_when_the_response_replays_its_own()
    {
        ReplayTestDbContext db = ReplayTestDbContext.New();
        (Guid channel, string eventId, Widget alertBox, Widget ttsWidget) = SeedCaptures(db);
        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();

        int pushed = await new RenderedAlertReplayer(db, notifier).ResendAsync(
            channel,
            eventId,
            includeTts: false
        );

        pushed.Should().Be(1);
        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                alertBox.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
        await notifier
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<string>(),
                ttsWidget.Id.ToString(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Replay_reports_every_count_the_replay_service_performed()
    {
        IActivityReplayService replay = Substitute.For<IActivityReplayService>();
        Guid channel = Guid.CreateVersion7();
        string eventId = Guid.CreateVersion7().ToString();
        replay
            .ReplayAsync(channel, eventId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ActivityReplayResult(6, 6, 2, 1, 3)));

        IActionResult result = await BuildController(replay)
            .ReplayActivity(channel.ToString(), eventId, CancellationToken.None);

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should()
            .BeOfType<StatusResponseDto<DashboardController.ReplayActivityResultDto>>()
            .Subject.Data.Should()
            .Be(new DashboardController.ReplayActivityResultDto(6, 6, 2, 1, 3));
    }

    [Fact]
    public async Task Replay_for_an_event_with_nothing_to_replay_returns_a_distinguishable_not_found_failure()
    {
        IActivityReplayService replay = Substitute.For<IActivityReplayService>();
        Guid channel = Guid.CreateVersion7();
        string eventId = Guid.CreateVersion7().ToString();
        replay
            .ReplayAsync(channel, eventId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ActivityReplayResult>("Nothing to replay.", "NOT_FOUND"));

        IActionResult result = await BuildController(replay)
            .ReplayActivity(channel.ToString(), eventId, CancellationToken.None);

        NotFoundObjectResult notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        StatusResponseDto<object> body = notFound
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Message.Should().Be("Nothing to replay.");
    }
}

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over only <see cref="Widget"/>, <see cref="RenderedAlertCapture"/>
/// and <see cref="ChannelEvent"/> — on a real relational SQLite connection — for
/// <see cref="DashboardControllerReplayTests"/>. Everything else throws, since <c>ReplayActivity</c> never
/// reaches it. Mirrors the shape of <c>Hubs/WidgetTestDbContext.cs</c> and <c>ApiTestDbContext.cs</c>.
/// </summary>
internal sealed class ReplayTestDbContext : DbContext, IApplicationDbContext
{
    private readonly SqliteConnection _connection;

    private ReplayTestDbContext(
        DbContextOptions<ReplayTestDbContext> options,
        SqliteConnection connection
    )
        : base(options) => _connection = connection;

    public static ReplayTestDbContext New()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        ReplayTestDbContext db = new(
            new DbContextOptionsBuilder<ReplayTestDbContext>().UseSqlite(connection).Options,
            connection
        );
        db.Database.EnsureCreated();
        return db;
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<RenderedAlertCapture> RenderedAlertCaptures => Set<RenderedAlertCapture>();
    public DbSet<AlertQueueEntry> AlertQueueEntries => Set<AlertQueueEntry>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Widget>(e =>
        {
            e.HasKey(w => w.Id);
            e.Ignore(w => w.Channel);
            e.Property(w => w.EventSubscriptions)
                .HasConversion(
                    Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Converter<
                        List<string>
                    >(),
                    Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Comparer<
                        List<string>
                    >()
                );
            e.Property(w => w.Settings)
                .HasConversion(
                    Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Converter<
                        Dictionary<string, object>
                    >(),
                    Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Comparer<
                        Dictionary<string, object>
                    >()
                );
        });

        b.Entity<ChannelEvent>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Channel);
            e.Ignore(c => c.User);
        });

        foreach (Type entity in UnmappedEntities)
            b.Ignore(entity);

        Infrastructure.Platform.Persistence.Extensions.ProviderCompatibilityExtensions.ApplySqliteCompatibility(
            b
        );
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(Widget),
        typeof(RenderedAlertCapture),
        typeof(ChannelEvent),
    ];

    private static readonly IReadOnlyList<Type> UnmappedEntities =
    [
        .. typeof(IApplicationDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            )
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(t => !Mapped.Contains(t)),
    ];

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<User> Users => throw new NotSupportedException();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ErasureRequest> ErasureRequests => throw new NotSupportedException();
    public DbSet<Channel> Channels => throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.TenantLimitOverride> TenantLimitOverrides =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.EntitlementGrant> EntitlementGrants =>
        throw new NotSupportedException();
    public DbSet<PlatformConnection> PlatformConnections => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.Service> Services => throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.Command> Commands => throw new NotSupportedException();
    public DbSet<Domain.Rewards.Entities.Reward> Rewards => throw new NotSupportedException();
    public DbSet<Domain.Rewards.Entities.Redemption> Redemptions =>
        throw new NotSupportedException();
    public DbSet<Domain.Rewards.Entities.RedemptionTimer> RedemptionTimers =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.ChatTrigger> ChatTriggers =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.VoiceTrigger> VoiceTriggers =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.VoiceTranscriptSegment> VoiceTranscriptSegments =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ChannelModerationStanding> ChannelModerationStandings =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.SharedBanSettings> SharedBanSettings =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.SharedBanTrustedChannel> SharedBanTrustedChannels =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.NetworkNukeBatch> NetworkNukeBatches =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.MassBanBatch> MassBanBatches =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ModeratorMassBanOptIn> ModeratorMassBanOptIns =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.NetworkBlock> NetworkBlocks =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.UserModerationHistory> UserModerationHistories =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ModerationHistoryEntry> ModerationHistoryEntries =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.UserTrustScore> UserTrustScores =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ModerationEscalationPolicy> ModerationEscalationPolicies =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ModerationEscalationState> ModerationEscalationStates =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ChatFilter> ChatFilters =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ModerationQueueItem> ModerationQueueItems =>
        throw new NotSupportedException();
    public DbSet<Domain.Notifications.Entities.ActionRequiredDismissal> ActionRequiredDismissals =>
        throw new NotSupportedException();
    public DbSet<Domain.Trust.Entities.TrustPolicy> TrustPolicies =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.SpamDefensePolicy> SpamDefensePolicies =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.SpamDetection> SpamDetections =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.SpamCampaignRecord> SpamCampaigns =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.FollowBotBlock> FollowBotBlocks =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.LockdownWindowRecord> LockdownWindows =>
        throw new NotSupportedException();

    public DbSet<Domain.Moderation.Entities.SpamSignature> SpamSignatures =>
        throw new NotSupportedException();
    public DbSet<Domain.Community.Entities.ChatPoll> ChatPolls => throw new NotSupportedException();
    public DbSet<Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        throw new NotSupportedException();
    public DbSet<Domain.Quotes.Entities.Quote> Quotes => throw new NotSupportedException();
    public DbSet<Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        throw new NotSupportedException();
    public DbSet<Domain.PickLists.Entities.PickList> PickLists => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.EventSubSubscription> EventSubSubscriptions =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.EventSubConduit> EventSubConduits =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.EventSubConduitShard> EventSubConduitShards =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.IdempotencyKey> IdempotencyKeys =>
        throw new NotSupportedException();
    public DbSet<ChatMessage> ChatMessages => throw new NotSupportedException();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => throw new NotSupportedException();
    public DbSet<Domain.Giveaways.Entities.Giveaway> Giveaways => throw new NotSupportedException();
    public DbSet<Domain.Giveaways.Entities.GiveawayEntry> GiveawayEntries =>
        throw new NotSupportedException();
    public DbSet<Domain.Giveaways.Entities.GiveawayWinner> GiveawayWinners =>
        throw new NotSupportedException();
    public DbSet<Domain.Giveaways.Entities.GiveawayCodePool> GiveawayCodePools =>
        throw new NotSupportedException();
    public DbSet<Domain.Giveaways.Entities.GiveawayCode> GiveawayCodes =>
        throw new NotSupportedException();
    public DbSet<Domain.Stream.Entities.Stream> Streams => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.Configuration> Configurations =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.Storage> Storages => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.Record> Records => throw new NotSupportedException();
    public DbSet<Permission> Permissions => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<BotAccount> BotAccounts => throw new NotSupportedException();
    public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
    public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => throw new NotSupportedException();
    public DbSet<Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        throw new NotSupportedException();
    public DbSet<Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        throw new NotSupportedException();
    public DbSet<CryptoKey> CryptoKeys => throw new NotSupportedException();
    public DbSet<KeyUsageBinding> KeyUsageBindings => throw new NotSupportedException();
    public DbSet<Domain.EventStore.Entities.EventSubjectKey> EventSubjectKeys =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordLiveRoleConfig> DiscordLiveRoleConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
    public DbSet<Domain.Vts.Entities.VtsConnection> VtsConnections =>
        throw new NotSupportedException();
    public DbSet<Domain.Obs.Entities.ObsConnection> ObsConnections =>
        throw new NotSupportedException();
    public DbSet<Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsConfig> TtsConfigs => throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsVoice> TtsVoices => throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsLexiconEntry> TtsLexiconEntries =>
        throw new NotSupportedException();
    public DbSet<Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        throw new NotSupportedException();
    public DbSet<Pronoun> Pronouns => throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<Domain.Stream.Entities.ShoutoutOverride> ShoutoutOverrides =>
        throw new NotSupportedException();
    public DbSet<ComplianceAuditLog> ComplianceAuditLogs => throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.Timer> Timers => throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.EventResponse> EventResponses =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PlatformEventResponseDefault> PlatformEventResponseDefaults =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PlatformBuiltinReplyDefault> PlatformBuiltinReplyDefaults =>
        throw new NotSupportedException();
    public DbSet<Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.ScheduledPipelineTask> ScheduledPipelineTasks =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.Pipeline> Pipelines => throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PipelineTrigger> PipelineTriggers =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.PipelineRunState> PipelineRunStates =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.ChannelBuiltinCommand> ChannelBuiltinCommands =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.CommandCooldownState> CommandCooldownStates =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.NamedCounter> NamedCounters =>
        throw new NotSupportedException();
    public DbSet<Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        throw new NotSupportedException();
    public DbSet<Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        throw new NotSupportedException();
    public DbSet<Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        throw new NotSupportedException();
    public DbSet<Domain.Commands.Entities.CommandUsage> CommandUsages =>
        throw new NotSupportedException();
    public DbSet<Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();
    public DbSet<ChannelMembership> ChannelMemberships => throw new NotSupportedException();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<ActionDefinition> ActionDefinitions => throw new NotSupportedException();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => throw new NotSupportedException();
    public DbSet<PermitGrant> PermitGrants => throw new NotSupportedException();
    public DbSet<ChannelMissingScope> ChannelMissingScopes => throw new NotSupportedException();
    public DbSet<IamPermission> IamPermissions => throw new NotSupportedException();
    public DbSet<IamRole> IamRoles => throw new NotSupportedException();
    public DbSet<IamRolePermission> IamRolePermissions => throw new NotSupportedException();
    public DbSet<IamPrincipal> IamPrincipals => throw new NotSupportedException();
    public DbSet<IamRoleAssignment> IamRoleAssignments => throw new NotSupportedException();
    public DbSet<SecurityNotice> SecurityNotices => throw new NotSupportedException();
    public DbSet<IamAuditLog> IamAuditLogs => throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.EarningRule> EarningRules =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.CatalogItem> CatalogItems =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.GameConfig> GameConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.GamePlay> GamePlays => throw new NotSupportedException();
    public DbSet<Domain.Marketplace.Entities.InstalledBundle> InstalledBundles =>
        throw new NotSupportedException();
    public DbSet<Domain.PlatformContent.Entities.PlatformContentDefinition> PlatformContentDefinitions =>
        throw new NotSupportedException();
    public DbSet<Domain.PlatformContent.Entities.PlatformContentVersion> PlatformContentVersions =>
        throw new NotSupportedException();
    public DbSet<Domain.PlatformContent.Entities.PlatformContentPublishJob> PlatformContentPublishJobs =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.GameSession> GameSessions =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.SavingsJar> SavingsJars =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.JarContribution> JarContributions =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        throw new NotSupportedException();
    public DbSet<Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.BillingTier> BillingTiers =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.TierLimit> TierLimits => throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.PricedUnit> PricedUnits =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.Subscription> Subscriptions =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.Invoice> Invoices => throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.UsageRecord> UsageRecords =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        throw new NotSupportedException();
    public DbSet<Domain.Billing.Entities.InviteCode> InviteCodes =>
        throw new NotSupportedException();
    public DbSet<Domain.PlatformContent.Entities.PlatformAudioAsset> PlatformAudioAssets =>
        throw new NotSupportedException();
    public DbSet<Domain.Federation.Entities.FederationPeer> FederationPeers =>
        throw new NotSupportedException();
    public DbSet<Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        throw new NotSupportedException();
    public DbSet<Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        throw new NotSupportedException();
    public DbSet<Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        throw new NotSupportedException();
    public DbSet<Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.HttpEgressAllowlist> HttpEgressAllowlists =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.WatchSession> WatchSessions =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        throw new NotSupportedException();
    public DbSet<Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        throw new NotSupportedException();
    public DbSet<Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        throw new NotSupportedException();
    public DbSet<Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        throw new NotSupportedException();
    public DbSet<Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        throw new NotSupportedException();
    public DbSet<Domain.Sound.Entities.SoundClip> SoundClips => throw new NotSupportedException();
    public DbSet<Domain.Sound.Entities.ChannelAudioMix> ChannelAudioMixes =>
        throw new NotSupportedException();
    public DbSet<Domain.Assets.Entities.ChannelAsset> ChannelAssets =>
        throw new NotSupportedException();
    public DbSet<Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        throw new NotSupportedException();
    public DbSet<Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();
    public DbSet<WidgetVersion> WidgetVersions => throw new NotSupportedException();
    public DbSet<WidgetGalleryItem> WidgetGalleryItems => throw new NotSupportedException();
    public DbSet<WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
}
