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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves <c>DashboardController.ReplayActivity</c> (S-REPLAY-ENDPOINT) re-broadcasts a previously captured
/// widget-event push VERBATIM to whichever widgets currently subscribe to that event type, and never
/// disguises "nothing to replay" as a success. It only exercises <see cref="IWidgetNotifier"/> — a real
/// currency/loyalty/reward service is never constructed or injected, so there is nothing for replay to
/// double-run even by accident.
/// </summary>
public sealed class DashboardControllerReplayTests
{
    private static DashboardController Build(
        ReplayTestDbContext db,
        out IWidgetNotifier widgetNotifier
    )
    {
        widgetNotifier = Substitute.For<IWidgetNotifier>();
        return new(
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry>(),
            Substitute.For<NomNomzBot.Application.Identity.Services.IChannelService>(),
            db,
            Substitute.For<ITwitchChannelsApi>(),
            Substitute.For<ITwitchSubscriptionsApi>(),
            widgetNotifier,
            TimeProvider.System,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<DashboardController>>()
        );
    }

    [Fact]
    public async Task Replay_repushes_the_exact_captured_payload_to_every_currently_subscribed_widget()
    {
        ReplayTestDbContext db = ReplayTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        string channelEventId = Guid.CreateVersion7().ToString();

        db.ChannelEvents.Add(
            new()
            {
                Id = channelEventId,
                ChannelId = channel,
                Type = "channel.follow",
                Data = "{}",
            }
        );
        Widget alertBox = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Alert box",
            IsEnabled = true,
            EventSubscriptions = ["follow"],
        };
        db.Widgets.Add(alertBox);
        db.RenderedAlertCaptures.Add(
            new()
            {
                BroadcasterId = channel,
                EventType = "follow",
                Payload = """{"user":"PogChamp42","followedAt":"2026-08-29T12:00:00Z"}""",
                ChannelEventId = channelEventId,
            }
        );
        await db.SaveChangesAsync();

        DashboardController controller = Build(db, out IWidgetNotifier widgetNotifier);

        IActionResult result = await controller.ReplayActivity(
            channel.ToString(),
            channelEventId,
            CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<DashboardController.ReplayActivityResultDto> body = ok
            .Value.Should()
            .BeOfType<StatusResponseDto<DashboardController.ReplayActivityResultDto>>()
            .Subject;
        body.Data!.WidgetsNotified.Should().Be(1);

        // The recording fake proves the SendWidgetEventAsync call actually happened, with the exact recorded
        // payload untouched — not merely that the controller returned 200.
        await widgetNotifier
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
    }

    [Fact]
    public async Task Replay_for_an_event_with_no_capture_returns_a_distinguishable_not_found_failure()
    {
        ReplayTestDbContext db = ReplayTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        string channelEventId = Guid.CreateVersion7().ToString();

        db.ChannelEvents.Add(
            new()
            {
                Id = channelEventId,
                ChannelId = channel,
                Type = "channel.follow",
                Data = "{}",
            }
        );
        await db.SaveChangesAsync();

        DashboardController controller = Build(db, out IWidgetNotifier widgetNotifier);

        IActionResult result = await controller.ReplayActivity(
            channel.ToString(),
            channelEventId,
            CancellationToken.None
        );

        NotFoundObjectResult notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        StatusResponseDto<object> body = notFound
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Message.Should().NotBeNullOrWhiteSpace();

        // A real 404 failure never pushes to any widget — replay must not fabricate a broadcast for a
        // capture that never existed.
        await widgetNotifier
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default!, default!, default!, default);
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
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Widget>(e =>
        {
            e.HasKey(w => w.Id);
            e.Ignore(w => w.Channel);
            e.Property(w => w.EventSubscriptions)
                .HasConversion(
                    NomNomzBot.Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Converter<
                        List<string>
                    >(),
                    NomNomzBot.Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Comparer<
                        List<string>
                    >()
                );
            e.Property(w => w.Settings)
                .HasConversion(
                    NomNomzBot.Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Converter<
                        Dictionary<string, object>
                    >(),
                    NomNomzBot.Infrastructure.Platform.Persistence.Converters.JsonValueConverter.Comparer<
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

        NomNomzBot.Infrastructure.Platform.Persistence.Extensions.ProviderCompatibilityExtensions.ApplySqliteCompatibility(
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ConsentRecord> ConsentRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ErasureRequest> ErasureRequests =>
        throw new NotSupportedException();
    public DbSet<Channel> Channels => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelModerator> ChannelModerators =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.RedemptionTimer> RedemptionTimers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChatTrigger> ChatTriggers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChannelModerationStanding> ChannelModerationStandings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanSettings> SharedBanSettings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanTrustedChannel> SharedBanTrustedChannels =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.NetworkNukeBatch> NetworkNukeBatches =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserModerationHistory> UserModerationHistories =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserTrustScore> UserTrustScores =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationPolicy> ModerationEscalationPolicies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationState> ModerationEscalationStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChatFilter> ChatFilters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.PickLists.Entities.PickList> PickLists =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubSubscription> EventSubSubscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduit> EventSubConduits =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard> EventSubConduitShards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.IdempotencyKey> IdempotencyKeys =>
        throw new NotSupportedException();
    public DbSet<ChatMessage> ChatMessages => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Chat.Entities.YouTubeLiveChatBan> YouTubeLiveChatBans =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.Giveaway> Giveaways =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayEntry> GiveawayEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayWinner> GiveawayWinners =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCodePool> GiveawayCodePools =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCode> GiveawayCodes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Storage> Storages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Permission> Permissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.BotAccount> BotAccounts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.AuthSession> AuthSessions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.RefreshToken> RefreshTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey> IpcDevModeKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.KeyUsageBinding> KeyUsageBindings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventSubjectKey> EventSubjectKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordLiveRoleConfig> DiscordLiveRoleConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelSubscription> ChannelSubscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Vts.Entities.VtsConnection> VtsConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Obs.Entities.ObsConnection> ObsConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsConfig> TtsConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsLexiconEntry> TtsLexiconEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Pronoun> Pronouns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ComplianceAuditLog> ComplianceAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask> ScheduledPipelineTasks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineTrigger> PipelineTriggers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineRunState> PipelineRunStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand> ChannelBuiltinCommands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandCooldownState> CommandCooldownStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.NamedCounter> NamedCounters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandUsage> CommandUsages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMembership> ChannelMemberships =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride> ChannelActionOverrides =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.PermitGrant> PermitGrants =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPermission> IamPermissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRole> IamRoles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRolePermission> IamRolePermissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPrincipal> IamPrincipals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment> IamRoleAssignments =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.SecurityNotice> SecurityNotices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamAuditLog> IamAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.EarningRule> EarningRules =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogItem> CatalogItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameConfig> GameConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GamePlay> GamePlays =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle> InstalledBundles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameSession> GameSessions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJar> SavingsJars =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.JarContribution> JarContributions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.BillingTier> BillingTiers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.TierLimit> TierLimits =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Subscription> Subscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Invoice> Invoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.UsageRecord> UsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.InviteCode> InviteCodes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeer> FederationPeers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist> HttpEgressAllowlists =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.WatchSession> WatchSessions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Sound.Entities.SoundClip> SoundClips =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Assets.Entities.ChannelAsset> ChannelAssets =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetVersion> WidgetVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem> WidgetGalleryItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
}
