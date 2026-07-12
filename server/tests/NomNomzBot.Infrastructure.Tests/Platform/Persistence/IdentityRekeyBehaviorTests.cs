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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Transport;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Proves the load-bearing behaviors of the identity re-key (schema §1.1):
///   1. Tenant isolation — a query under tenant A returns ONLY A's rows (the IDOR guard).
///   2. Helix-id resolution — the value resolved for a Twitch call is the Twitch STRING id, never the Guid.
///   3. Soft-delete — a deleted row is filtered out, and <c>IgnoreQueryFilters</c> still sees it.
///   4. Identity round-trip — the Guid PK persists and the channel is found by its <c>TwitchChannelId</c>.
///
/// Uses a focused EF context that maps the re-keyed identity entities (User / Channel / Command) and wires
/// the SAME composing tenant + soft-delete global filter the production <c>AppDbContext</c> uses
/// (<see cref="ModelBuilderExtensions.ApplyTenantAndSoftDeleteFilters"/>), reading the ambient tenant at
/// query time. The full <c>AppDbContext</c> can't run on a test provider yet because its [VC:JSON] columns
/// still carry a Postgres-only <c>jsonb</c> mapping (a separate schema §1.4 converter slice); the filter and
/// resolver under test are provider-agnostic, so this proves the re-key behavior without that dependency.
/// </summary>
public sealed class IdentityRekeyBehaviorTests
{
    private sealed class FakeTenant : ICurrentTenantService
    {
        public Guid? BroadcasterId { get; private set; }
        public bool HasTenant => BroadcasterId.HasValue;

        public void SetTenant(Guid broadcasterId) => BroadcasterId = broadcasterId;

        public void Clear() => BroadcasterId = null;
    }

    /// <summary>Focused context over the re-keyed identity entities; same composing global filter as prod.</summary>
    private sealed class RekeyTestContext : DbContext
    {
        private readonly ICurrentTenantService _tenant;

        public RekeyTestContext(
            DbContextOptions<RekeyTestContext> options,
            ICurrentTenantService tenant
        )
            : base(options) => _tenant = tenant;

        public DbSet<User> Users => Set<User>();
        public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
            Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
        public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
        public DbSet<Channel> Channels => Set<Channel>();
        public DbSet<Command> Commands => Set<Command>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            // Keep the model to exactly the three re-keyed entities under test — ignore everything
            // reachable through navigations that this focused context does not map.
            b.Ignore<ChannelModerator>();
            b.Ignore<NomNomzBot.Domain.Stream.Entities.Stream>();
            b.Ignore<ChannelEvent>();

            b.Entity<User>().HasKey(e => e.Id);
            b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);
            b.Entity<User>().HasIndex(e => e.TwitchUserId).IsUnique();

            b.Entity<Channel>().HasKey(e => e.Id);
            b.Entity<Channel>().HasIndex(e => e.TwitchChannelId).IsUnique();
            b.Entity<Channel>().HasIndex(e => e.OwnerUserId).IsUnique();
            // The List<string>/List<string> JSON columns are not exercised by these tests.
            b.Entity<Channel>().Ignore(e => e.Tags).Ignore(e => e.ContentLabels);
            b.Entity<Channel>()
                .Ignore(e => e.Moderators)
                .Ignore(e => e.Streams)
                .Ignore(e => e.Events);

            b.Entity<Command>().HasKey(e => e.Id);
            b.Entity<Command>().Ignore(e => e.TemplateResponses).Ignore(e => e.Aliases);
            b.Entity<Command>().Ignore(e => e.Channel);

            // The unit under test: the production composing tenant + soft-delete global query filter.
            b.ApplyTenantAndSoftDeleteFilters(() => _tenant.BroadcasterId);
        }
    }

    private static readonly Guid TenantA = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly Guid TenantB = Guid.Parse("0192a000-0000-7000-8000-00000000b002");
    private const string TwitchChannelA = "11111111";
    private const string TwitchChannelB = "22222222";

    private static RekeyTestContext NewContext(string dbName, ICurrentTenantService tenant)
    {
        DbContextOptions<RekeyTestContext> options = new DbContextOptionsBuilder<RekeyTestContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RekeyTestContext(options, tenant);
    }

    private static async Task SeedTwoTenantsAsync(string dbName)
    {
        await using RekeyTestContext db = NewContext(dbName, new FakeTenant());

        Guid ownerA = Guid.CreateVersion7();
        Guid ownerB = Guid.CreateVersion7();
        db.Users.Add(
            new()
            {
                Id = ownerA,
                TwitchUserId = "u-a",
                Username = "alpha",
                UsernameNormalized = "alpha",
                DisplayName = "Alpha",
            }
        );
        db.Users.Add(
            new()
            {
                Id = ownerB,
                TwitchUserId = "u-b",
                Username = "bravo",
                UsernameNormalized = "bravo",
                DisplayName = "Bravo",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = TenantA,
                OwnerUserId = ownerA,
                TwitchChannelId = TwitchChannelA,
                Name = "alpha",
                NameNormalized = "alpha",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = TenantB,
                OwnerUserId = ownerB,
                TwitchChannelId = TwitchChannelB,
                Name = "bravo",
                NameNormalized = "bravo",
            }
        );
        db.Commands.Add(
            new()
            {
                BroadcasterId = TenantA,
                Name = "hello-a",
                NameNormalized = "hello-a",
                MinPermissionLevel = 0,
                Tier = "template",
            }
        );
        db.Commands.Add(
            new()
            {
                BroadcasterId = TenantB,
                Name = "hello-b",
                NameNormalized = "hello-b",
                MinPermissionLevel = 0,
                Tier = "template",
            }
        );
        await db.SaveChangesAsync();
    }

    // ─── 1. Tenant isolation (the IDOR guard) ──────────────────────────────────────

    [Fact]
    public async Task Query_AsTenantA_ReturnsOnlyTenantA_Rows()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        FakeTenant tenant = new();
        tenant.SetTenant(TenantA);
        await using RekeyTestContext db = NewContext(dbName, tenant);

        List<Command> visible = await db.Commands.ToListAsync();

        visible.Should().HaveCount(1);
        visible.Single().Name.Should().Be("hello-a");
        visible.Single().BroadcasterId.Should().Be(TenantA);
        visible.Should().NotContain(c => c.BroadcasterId == TenantB);
    }

    [Fact]
    public async Task Query_AsTenantB_ReturnsOnlyTenantB_Rows()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        FakeTenant tenant = new();
        tenant.SetTenant(TenantB);
        await using RekeyTestContext db = NewContext(dbName, tenant);

        List<Command> visible = await db.Commands.ToListAsync();

        visible.Should().ContainSingle(c => c.Name == "hello-b" && c.BroadcasterId == TenantB);
        visible.Should().NotContain(c => c.BroadcasterId == TenantA);
    }

    [Fact]
    public async Task Query_AsTenantA_CannotFetchTenantB_RowByKnownId_IDOR()
    {
        // An attacker scoped to tenant A who has learned tenant B's exact command id still cannot read it:
        // the global tenant filter excludes B's rows from every A-scoped query (the direct IDOR guard,
        // complementing ChannelAccessService denying A from resolving B's tenant in the first place).
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        FakeTenant tenant = new();
        await using RekeyTestContext db = NewContext(dbName, tenant);

        // B's command id, looked up bypassing the filter (simulating an id the attacker leaked/guessed).
        Guid bCommandId = (
            await db.Commands.IgnoreQueryFilters().SingleAsync(c => c.BroadcasterId == TenantB)
        ).Id;

        // Scoped to A, a direct fetch of B's row by its exact id must come back empty.
        tenant.SetTenant(TenantA);
        Command? leaked = await db.Commands.FirstOrDefaultAsync(c => c.Id == bCommandId);

        leaked.Should().BeNull();
    }

    [Fact]
    public async Task Query_NoAmbientTenant_SeesAllTenants()
    {
        // A background/cross-tenant context (no tenant set) reads all rows — the filter's null-tenant branch.
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        await using RekeyTestContext db = NewContext(dbName, new FakeTenant());

        (await db.Commands.CountAsync()).Should().Be(2);
    }

    // ─── 2. Helix-id resolution — Twitch receives the STRING id, never the Guid ─────

    [Fact]
    public async Task Resolver_TenantGuid_ResolvesToTwitchStringId_NotTheGuid()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        await using RekeyTestContext ctx = NewContext(dbName, new FakeTenant());
        ITwitchIdentityResolver resolver = new TwitchIdentityResolver(new ResolverDb(ctx));

        string? outgoing = await resolver.GetTwitchChannelIdAsync(TenantA);

        outgoing.Should().Be(TwitchChannelA);
        outgoing.Should().NotBe(TenantA.ToString());
        Guid.TryParse(outgoing, out _)
            .Should()
            .BeFalse("the outgoing Helix id is a Twitch numeric string, not a Guid");
    }

    [Fact]
    public async Task Resolver_InboundTwitchId_ResolvesBackToTenantGuid()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        await using RekeyTestContext ctx = NewContext(dbName, new FakeTenant());
        ITwitchIdentityResolver resolver = new TwitchIdentityResolver(new ResolverDb(ctx));

        (await resolver.GetBroadcasterIdAsync(TwitchChannelB)).Should().Be(TenantB);
        (await resolver.GetBroadcasterIdAsync("does-not-exist")).Should().BeNull();
        (await resolver.GetTwitchUserIdAsync(await OwnerOf(ctx, TenantA))).Should().Be("u-a");
    }

    // ─── 3. Soft-delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SoftDeletedRow_IsFiltered_ButVisibleWithIgnoreQueryFilters()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        FakeTenant tenantA = new();
        tenantA.SetTenant(TenantA);
        await using (RekeyTestContext db = NewContext(dbName, tenantA))
        {
            Command cmd = await db.Commands.SingleAsync();
            cmd.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (RekeyTestContext db = NewContext(dbName, tenantA))
        {
            (await db.Commands.AnyAsync()).Should().BeFalse();

            Command? raw = await db
                .Commands.IgnoreQueryFilters()
                .SingleOrDefaultAsync(c => c.BroadcasterId == TenantA);
            raw.Should().NotBeNull();
            raw!.DeletedAt.Should().NotBeNull();
        }
    }

    // ─── 4. Identity round-trip ────────────────────────────────────────────────────

    [Fact]
    public async Task Channel_GuidPk_Persists_AndIsQueryableByTwitchChannelId()
    {
        string dbName = Guid.NewGuid().ToString();
        await SeedTwoTenantsAsync(dbName);

        await using RekeyTestContext db = NewContext(dbName, new FakeTenant());

        Channel? byId = await db.Channels.FirstOrDefaultAsync(c => c.Id == TenantA);
        byId.Should().NotBeNull();
        byId!.Id.Should().Be(TenantA);
        byId.TwitchChannelId.Should().Be(TwitchChannelA);

        Channel? byTwitchId = await db.Channels.FirstOrDefaultAsync(c =>
            c.TwitchChannelId == TwitchChannelB
        );
        byTwitchId.Should().NotBeNull();
        byTwitchId!.Id.Should().Be(TenantB);
    }

    private static async Task<Guid> OwnerOf(RekeyTestContext ctx, Guid channelId) =>
        await ctx.Channels.Where(c => c.Id == channelId).Select(c => c.OwnerUserId).SingleAsync();

    /// <summary>Adapts the focused context to the <see cref="IApplicationDbContext"/> surface the resolver reads.</summary>
    private sealed class ResolverDb(RekeyTestContext inner) : IApplicationDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
            inner.Redemptions;
        public DbSet<ConsentRecord> ConsentRecords => inner.ConsentRecords;
        public DbSet<Channel> Channels => inner.Channels;
        public DbSet<Command> Commands => inner.Commands;

        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            inner.SaveChangesAsync(ct);

        // The resolver only reads Users/Channels; the remaining surface is unused here.
        public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelModerator> ChannelModerators =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Widgets.Entities.Widget> Widgets =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubSubscription> EventSubSubscriptions =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduit> EventSubConduits =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard> EventSubConduitShards =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.IdempotencyKey> IdempotencyKeys =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Chat.Entities.ChatMessage> ChatMessages =>
            throw new NotSupportedException();
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
        public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelEvent> ChannelEvents =>
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
        public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelSubscription> ChannelSubscriptions =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Identity.Entities.Pronoun> Pronouns =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
            throw new NotSupportedException();
        public DbSet<PipelineStep> PipelineSteps => throw new NotSupportedException();
        public DbSet<PipelineStepCondition> PipelineStepConditions =>
            throw new NotSupportedException();
        public DbSet<PipelineExecution> PipelineExecutions => throw new NotSupportedException();
        public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands =>
            throw new NotSupportedException();
        public DbSet<CommandCooldownState> CommandCooldownStates =>
            throw new NotSupportedException();
        public DbSet<NamedCounter> NamedCounters => throw new NotSupportedException();
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
        public DbSet<CommandUsage> CommandUsages => throw new NotSupportedException();
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
        public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
            throw new NotSupportedException();
        public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
            throw new NotSupportedException();
    }
}
