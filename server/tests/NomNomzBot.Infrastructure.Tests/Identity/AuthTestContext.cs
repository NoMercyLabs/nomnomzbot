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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Shared scaffolding for the auth behavior tests: a focused EF context mapping only the auth/integration
/// entities (so it runs on the InMemory provider, where the production <c>AppDbContext</c>'s
/// jsonb-of-complex-type columns cannot materialize), the REAL envelope-encryption crypto stack
/// (so the vault round-trip proves ciphertext-at-rest, not a stub), and a recording event bus.
/// </summary>
internal static class AuthTestBuilder
{
    // A fixed 32-byte base64 deployment key drives the deterministic KEK fallback (no OS keystore needed).
    private const string ConfigKey = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";

    /// <summary>
    /// Builds the real token protector over the real envelope crypto stack, backed by the persisted DEK store
    /// (<see cref="CryptoKeySubjectKeyStore"/>) over <paramref name="db"/>. Passing the same context the vault uses
    /// keeps the DEK registry and the ciphertext that references it in one store — exactly the production wiring.
    /// </summary>
    public static ITokenProtector RealTokenProtector(
        IApplicationDbContext db,
        out ISubjectKeyService subjectKeys
    )
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = ConfigKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(db);
        subjectKeys = new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
        return new TokenProtector(subjectKeys, NullLogger<TokenProtector>.Instance);
    }

    public static AuthDbContext NewContext() => NewContext(Guid.NewGuid().ToString());

    /// <summary>
    /// A context over a named in-memory store. Two contexts built with the SAME name share one backing store —
    /// the test analogue of a process restart against the same persisted database.
    /// </summary>
    public static AuthDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options);

    /// <summary>
    /// A real <see cref="ISystemCredentialsProvider"/> over the test context + REAL token protector, so a
    /// test proves the DB-vaulted-first → config-fallback resolution and the AAD binding for real (no stub).
    /// Builds a minimal <see cref="ServiceCollection"/> to supply an <see cref="IServiceScopeFactory"/> that
    /// hands back the same <paramref name="db"/> + <paramref name="protector"/> instances from inner scopes —
    /// matching the production wiring without needing a full DI host.
    /// </summary>
    public static ISystemCredentialsProvider CredentialsProvider(
        AuthDbContext db,
        ITokenProtector protector,
        IConfiguration configuration
    )
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITokenProtector>(protector);
        ServiceProvider sp = services.BuildServiceProvider();
        return new SystemCredentialsProvider(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuration
        );
    }
}

/// <summary>Records every published domain event so a test can assert the side effect actually fired.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IDomainEvent> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent => Published.Add(@event);
}

/// <summary>
/// Focused EF context over the auth/integration entities. Maps only what the services under test touch;
/// every other <see cref="IApplicationDbContext"/> member throws, since the tests never reach them.
/// </summary>
internal sealed class AuthDbContext : DbContext, IApplicationDbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        Set<NomNomzBot.Domain.Identity.Entities.CryptoKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(e => e.Id);
        b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);

        b.Entity<Channel>().HasKey(e => e.Id);
        b.Entity<Channel>().Ignore(e => e.Tags).Ignore(e => e.ContentLabels);
        b.Entity<Channel>()
            .Ignore(e => e.User)
            .Ignore(e => e.Moderators)
            .Ignore(e => e.Streams)
            .Ignore(e => e.Events);

        b.Entity<AuthSession>().HasKey(e => e.Id);
        b.Entity<AuthSession>()
            .Ignore(e => e.User)
            .Ignore(e => e.Channel)
            .Ignore(e => e.RefreshTokens);

        b.Entity<RefreshToken>().HasKey(e => e.Id);
        b.Entity<RefreshToken>().Ignore(e => e.Session).Ignore(e => e.User);

        b.Entity<IntegrationConnection>().HasKey(e => e.Id);
        b.Entity<IntegrationConnection>().Ignore(e => e.Channel).Ignore(e => e.Tokens);

        b.Entity<IntegrationToken>().HasKey(e => e.Id);
        b.Entity<IntegrationToken>().Ignore(e => e.Connection).Ignore(e => e.Channel);

        // The persisted DEK registry (scalar-only) — mapped so the vault/protector tests seal and re-open tokens
        // through the same store the production wiring uses, and so the restart-survival test can prove it.
        b.Entity<NomNomzBot.Domain.Identity.Entities.CryptoKey>().HasKey(e => e.Id);

        // Reactive missing-scope rows (scalar; nav ignored) — mapped so the ScopeNotificationService tests seed
        // + query gaps through this harness.
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>().Ignore(e => e.Channel);

        // System-config table (scalar Key/Value/SecureValue) — mapped so the system-credentials provider
        // tests can seed wizard-vaulted rows and prove the DB-first resolution + AAD binding.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().Ignore(e => e.Channel);

        // Mapped standalone (navs ignored, Channel.Moderators already ignored above) so the
        // ChannelAccessService tests can exercise the moderator-grant branch of tenant resolution.
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelModerator>()
            .HasKey(e => new { e.ChannelId, e.UserId });
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelModerator>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.User);

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing
        // getter bodies, then tries to map their jsonb-of-complex-type columns (unsupported on InMemory).
        // Ignore every entity these tests do not exercise so the model stays minimal and provider-agnostic.
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Service>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Command>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.Widget>();

        // Reward is scalar-only (no jsonb-of-complex-type column), so it materializes on InMemory. Mapped
        // (navs ignored) so the reward-sync tests can prove the Twitch read path through this harness.
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().Ignore(e => e.Channel);
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();
        b.Ignore<NomNomzBot.Domain.Chat.Entities.ChatMessage>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelEvent>();
        b.Ignore<NomNomzBot.Domain.Stream.Entities.Stream>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Storage>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Permission>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.BotAccount>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelSubscription>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Pronoun>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.EventResponse>();
        b.Ignore<NomNomzBot.Domain.Rewards.Entities.WatchStreak>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Pipeline>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelModerator> ChannelModerators =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelModerator>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        Set<NomNomzBot.Domain.Rewards.Entities.Reward>();
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelEvent> ChannelEvents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey> IpcDevModeKeys =>
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
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand> ChannelBuiltinCommands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandCooldownState> CommandCooldownStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.NamedCounter> NamedCounters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandUsage> CommandUsages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();

    // Roles & permissions (Plane A/B) — mapped (simple scalar/enum entities materialize on InMemory) so the
    // role-resolver tests can seed/query them through this harness.
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
        Set<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>();

    // Platform IAM (Plane C) — mapped (simple scalar entities) so the IAM-service tests seed through this harness.
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

    // Economy — currency core (mapped so the economy-service tests can seed/query through this harness).
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
}
