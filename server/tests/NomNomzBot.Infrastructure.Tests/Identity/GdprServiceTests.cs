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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Identity;
// Disambiguate the domain Record entity from xunit's Record helper (the test project globally imports Xunit).
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves GDPR erasure (right to be forgotten) actually clears the user's vaulted OAuth tokens — the real
/// store — not just the legacy <c>Service</c> rows: the subject's vault connection flips to revoked and its
/// token ciphertext is soft-deleted, a different user's connection is left untouched (so erasure targets the
/// right subject), and the audit log records the connections it cleared.
/// </summary>
public sealed class GdprServiceTests
{
    private static readonly Guid SubjectUser = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly Guid OtherUser = Guid.Parse("0192a000-0000-7000-8000-00000000a002");
    private static readonly Guid SubjectChannel = Guid.Parse(
        "0192a000-0000-7000-8000-00000000c001"
    );
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000c002");

    private static (GdprService Sut, IntegrationTokenVault Vault, GdprTestDbContext Db) Build()
    {
        GdprTestDbContext db = new(
            new DbContextOptionsBuilder<GdprTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new NoopScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        GdprService sut = new(db, vault, TimeProvider.System, NullLogger<GdprService>.Instance);
        return (sut, vault, db);
    }

    private static async Task<Guid> StoreConnectionAsync(
        IntegrationTokenVault vault,
        Guid channel,
        Guid connectedBy,
        string accessToken
    )
    {
        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    channel,
                    AuthEnums.IntegrationProvider.Twitch,
                    "twitch-account",
                    "login",
                    ["channel:read:subscriptions"],
                    ClientId: "client",
                    IsByok: false,
                    ConnectedByUserId: connectedBy,
                    SettingsJson: null
                )
            )
        )
            .Value
            .Id;

        await vault.StoreTokensAsync(
            connectionId,
            new StoreTokensDto(accessToken, "refresh", AppToken: null, DateTime.UtcNow.AddHours(1)),
            ["channel:read:subscriptions"]
        );
        return connectionId;
    }

    [Fact]
    public async Task DeleteUserData_RevokesTheSubjectsVaultedConnection_AndLeavesAnotherUsersIntact()
    {
        (GdprService sut, IntegrationTokenVault vault, GdprTestDbContext db) = Build();
        db.Users.Add(
            new User
            {
                Id = SubjectUser,
                TwitchUserId = "tw-subject",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        db.Users.Add(
            new User
            {
                Id = OtherUser,
                TwitchUserId = "tw-other",
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        await db.SaveChangesAsync();

        Guid subjectConn = await StoreConnectionAsync(
            vault,
            SubjectChannel,
            SubjectUser,
            "subject-access-token"
        );
        Guid otherConn = await StoreConnectionAsync(
            vault,
            OtherChannel,
            OtherUser,
            "other-access-token"
        );

        Result result = await sut.DeleteUserDataAsync(SubjectUser.ToString());

        result.IsSuccess.Should().BeTrue();

        // The subject's connection is revoked and its token ciphertext soft-deleted.
        IntegrationConnection subject = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == subjectConn);
        subject.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);
        List<IntegrationToken> subjectTokens = await db
            .IntegrationTokens.IgnoreQueryFilters()
            .Where(t => t.ConnectionId == subjectConn)
            .ToListAsync();
        subjectTokens.Should().NotBeEmpty().And.OnlyContain(t => t.DeletedAt != null);

        // The other user's connection and tokens are untouched — erasure targeted only the subject.
        IntegrationConnection other = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == otherConn);
        other.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        List<IntegrationToken> otherTokens = await db
            .IntegrationTokens.IgnoreQueryFilters()
            .Where(t => t.ConnectionId == otherConn)
            .ToListAsync();
        otherTokens.Should().NotBeEmpty().And.OnlyContain(t => t.DeletedAt == null);

        // The subject profile is anonymized and the erasure is audited with the cleared connection counted.
        User anonymized = await db.Users.SingleAsync(u => u.Id == SubjectUser);
        anonymized.Enabled.Should().BeFalse();
        anonymized.DisplayName.Should().Be("Deleted User");
        // The indexed normalized username is anonymized too — it must not leak the original login.
        anonymized.UsernameNormalized.Should().NotBe("subject");

        DeletionAuditLog audit = await db.DeletionAuditLogs.SingleAsync();
        audit.TablesAffected.Should().Contain("IntegrationConnections");
        audit.RowsDeleted.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task DeleteUserData_WithNoVaultConnections_StillSucceeds()
    {
        (GdprService sut, _, GdprTestDbContext db) = Build();
        db.Users.Add(
            new User
            {
                Id = SubjectUser,
                TwitchUserId = "tw-subject",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        await db.SaveChangesAsync();

        Result result = await sut.DeleteUserDataAsync(SubjectUser.ToString());

        result.IsSuccess.Should().BeTrue();
        (await db.Users.SingleAsync(u => u.Id == SubjectUser)).Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteUserData_ScrubsTheSubjectsViewerData_AcrossChannels_AndLeavesOthers()
    {
        (GdprService sut, _, GdprTestDbContext db) = Build();
        db.Users.Add(
            new User
            {
                Id = SubjectUser,
                TwitchUserId = "tw-subject",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        db.Users.Add(
            new User
            {
                Id = OtherUser,
                TwitchUserId = "tw-other",
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        // Two channels' worth of subject data — one row already soft-deleted — plus another viewer's row.
        db.ViewerData.AddRange(
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = SubjectChannel,
                ViewerUserId = SubjectUser,
                Key = "deaths",
                Value = "12",
            },
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = OtherChannel,
                ViewerUserId = SubjectUser,
                Key = "quest",
                Value = "done",
                DeletedAt = DateTime.UtcNow,
            },
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = SubjectChannel,
                ViewerUserId = OtherUser,
                Key = "deaths",
                Value = "3",
            }
        );
        await db.SaveChangesAsync();

        Result result = await sut.DeleteUserDataAsync(SubjectUser.ToString());

        result.IsSuccess.Should().BeTrue();
        // Erasure is a HARD delete — even the already-soft-deleted row is gone; the other viewer keeps theirs.
        List<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> remaining = await db
            .ViewerData.IgnoreQueryFilters()
            .ToListAsync();
        remaining.Should().ContainSingle().Which.ViewerUserId.Should().Be(OtherUser);

        DeletionAuditLog audit = await db.DeletionAuditLogs.SingleAsync();
        audit.TablesAffected.Should().Contain("ViewerData");
        audit.RowsDeleted.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for these tests.</summary>
    private sealed class NoopScopeGrant : IScopeGrantService
    {
        public IReadOnlyList<string> RequiredScopesFor(string featureKey) => [];

        public Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
            Guid broadcasterId,
            string featureKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScopeGrantState(true, null, [])));

        public Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
            Guid connectionId,
            IReadOnlyList<string> actualScopes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>(actualScopes));
    }
}

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> for the GDPR erasure tests, mapping only the entities
/// <c>DeleteUserDataAsync</c>/<c>ExportUserDataAsync</c> touch (with their complex/jsonb columns and
/// navigations ignored so the model materializes on the InMemory provider) and ignoring every other entity.
/// </summary>
internal sealed class GdprTestDbContext : DbContext, IApplicationDbContext
{
    public GdprTestDbContext(DbContextOptions<GdprTestDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<RedemptionTimer> RedemptionTimers => throw new NotSupportedException();
    public DbSet<ChatTrigger> ChatTriggers => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => Set<YouTubeLiveChatBan>();
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
    public DbSet<Record> Records => Set<Record>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => Set<DeletionAuditLog>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        Set<NomNomzBot.Domain.Identity.Entities.CryptoKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(e => e.Id);
        b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);

        b.Entity<ChatMessage>().HasKey(e => e.Id);
        b.Entity<ChatMessage>()
            .Ignore(e => e.Fragments)
            .Ignore(e => e.Badges)
            .Ignore(e => e.Channel)
            .Ignore(e => e.Stream);

        b.Entity<Record>().HasKey(e => e.Id);
        b.Entity<Record>().Ignore(e => e.Channel);

        b.Entity<Service>().HasKey(e => e.Id);
        b.Entity<Service>().Ignore(e => e.Channel);

        b.Entity<DeletionAuditLog>().HasKey(e => e.Id);

        b.Entity<IntegrationConnection>().HasKey(e => e.Id);
        b.Entity<IntegrationConnection>().Ignore(e => e.Channel).Ignore(e => e.Tokens);

        b.Entity<IntegrationToken>().HasKey(e => e.Id);
        b.Entity<IntegrationToken>().Ignore(e => e.Connection).Ignore(e => e.Channel);

        // The persisted DEK registry (scalar-only) — the vault seals/crypto-shreds tokens through it.
        b.Entity<NomNomzBot.Domain.Identity.Entities.CryptoKey>().HasKey(e => e.Id);

        // Everything else the IApplicationDbContext surface exposes is ignored — these tests never reach it,
        // and EF would otherwise try to map unsupported jsonb-of-complex-type columns on InMemory.
        b.Ignore<ChannelModerator>();
        b.Ignore<Channel>();
        b.Ignore<Reward>();
        b.Ignore<NomNomzBot.Domain.Quotes.Entities.Quote>();
        b.Ignore<NomNomzBot.Domain.PickLists.Entities.PickList>();
        b.Ignore<Widget>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetVersion>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent>();
        b.Ignore<EventSubSubscription>();
        b.Ignore<EventSubConduit>();
        b.Ignore<EventSubConduitShard>();
        b.Ignore<IdempotencyKey>();
        b.Ignore<ChannelEvent>();
        b.Ignore<global::NomNomzBot.Domain.Stream.Entities.Stream>();
        b.Ignore<Storage>();
        b.Ignore<Permission>();
        b.Ignore<ChannelFeature>();
        b.Ignore<ChannelBotAuthorization>();
        b.Ignore<BotAccount>();
        b.Ignore<AuthSession>();
        b.Ignore<RefreshToken>();
        b.Ignore<IpcDevModeKey>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
        b.Ignore<ChannelSubscription>();
        b.Ignore<Configuration>();
        b.Ignore<Command>();
        b.Ignore<Pronoun>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        b.Ignore<EventResponse>();
        b.Ignore<WatchStreak>();
        b.Ignore<Pipeline>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        Set<NomNomzBot.Domain.Quotes.Entities.Quote>();
    public DbSet<NomNomzBot.Domain.PickLists.Entities.PickList> PickLists =>
        throw new NotSupportedException();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetVersion> WidgetVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem> WidgetGalleryItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<global::NomNomzBot.Domain.Stream.Entities.Stream>();
    public DbSet<Storage> Storages => Set<Storage>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ChannelFeature> ChannelFeatures => Set<ChannelFeature>();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        Set<ChannelBotAuthorization>();
    public DbSet<BotAccount> BotAccounts => Set<BotAccount>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => Set<IpcDevModeKey>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();
    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        Set<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        Set<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        Set<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        Set<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        Set<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();
    public DbSet<WatchStreak> WatchStreaks => Set<WatchStreak>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<PipelineStepCondition> PipelineStepConditions => Set<PipelineStepCondition>();
    public DbSet<PipelineExecution> PipelineExecutions => Set<PipelineExecution>();
    public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands => Set<ChannelBuiltinCommand>();
    public DbSet<CommandCooldownState> CommandCooldownStates => Set<CommandCooldownState>();
    public DbSet<NamedCounter> NamedCounters => Set<NamedCounter>();
    public DbSet<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        Set<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        Set<NomNomzBot.Domain.Engagement.Entities.EngagementConfig>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        Set<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest>();
    public DbSet<CommandUsage> CommandUsages => Set<CommandUsage>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        Set<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        Set<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        Set<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMembership> ChannelMemberships =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelMembership>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        Set<NomNomzBot.Domain.Identity.Entities.ActionDefinition>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride> ChannelActionOverrides =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.PermitGrant> PermitGrants =>
        Set<NomNomzBot.Domain.Identity.Entities.PermitGrant>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPermission> IamPermissions =>
        Set<NomNomzBot.Domain.Identity.Entities.IamPermission>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRole> IamRoles =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRole>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRolePermission> IamRolePermissions =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRolePermission>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPrincipal> IamPrincipals =>
        Set<NomNomzBot.Domain.Identity.Entities.IamPrincipal>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment> IamRoleAssignments =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamAuditLog> IamAuditLogs =>
        Set<NomNomzBot.Domain.Identity.Entities.IamAuditLog>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.EarningRule> EarningRules =>
        Set<NomNomzBot.Domain.Economy.Entities.EarningRule>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyAccount>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogItem> CatalogItems =>
        Set<NomNomzBot.Domain.Economy.Entities.CatalogItem>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        Set<NomNomzBot.Domain.Economy.Entities.CatalogPurchase>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameConfig> GameConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.GameConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GamePlay> GamePlays =>
        Set<NomNomzBot.Domain.Economy.Entities.GamePlay>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        Set<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJar> SavingsJars =>
        Set<NomNomzBot.Domain.Economy.Entities.SavingsJar>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        Set<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.JarContribution> JarContributions =>
        Set<NomNomzBot.Domain.Economy.Entities.JarContribution>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.BillingTier> BillingTiers =>
        Set<NomNomzBot.Domain.Billing.Entities.BillingTier>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.TierLimit> TierLimits =>
        Set<NomNomzBot.Domain.Billing.Entities.TierLimit>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Subscription> Subscriptions =>
        Set<NomNomzBot.Domain.Billing.Entities.Subscription>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Invoice> Invoices =>
        Set<NomNomzBot.Domain.Billing.Entities.Invoice>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.UsageRecord> UsageRecords =>
        Set<NomNomzBot.Domain.Billing.Entities.UsageRecord>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        Set<NomNomzBot.Domain.Billing.Entities.FoundersBadge>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.InviteCode> InviteCodes =>
        Set<NomNomzBot.Domain.Billing.Entities.InviteCode>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeer> FederationPeers =>
        Set<NomNomzBot.Domain.Federation.Entities.FederationPeer>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        Set<NomNomzBot.Domain.Federation.Entities.FederationPeerKey>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        Set<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        Set<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        Set<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        Set<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist> HttpEgressAllowlists =>
        Set<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        Set<NomNomzBot.Domain.Analytics.Entities.ViewerProfile>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.WatchSession> WatchSessions =>
        Set<NomNomzBot.Domain.Analytics.Entities.WatchSession>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        Set<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlag>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScript>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion>();
    public DbSet<NomNomzBot.Domain.Sound.Entities.SoundClip> SoundClips =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();
}
