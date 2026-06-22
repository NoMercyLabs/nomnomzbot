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
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<ConsentRecord> ConsentRecords { get; }
    DbSet<Channel> Channels { get; }
    DbSet<ChannelModerator> ChannelModerators { get; }
    DbSet<Service> Services { get; }
    DbSet<Command> Commands { get; }
    DbSet<Reward> Rewards { get; }
    DbSet<Widget> Widgets { get; }
    DbSet<EventSubSubscription> EventSubSubscriptions { get; }
    DbSet<EventSubConduit> EventSubConduits { get; }
    DbSet<EventSubConduitShard> EventSubConduitShards { get; }
    DbSet<IdempotencyKey> IdempotencyKeys { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<ChannelEvent> ChannelEvents { get; }
    DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams { get; }
    DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations { get; }
    DbSet<Storage> Storages { get; }
    DbSet<Record> Records { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<ChannelFeature> ChannelFeatures { get; }
    DbSet<ChannelBotAuthorization> ChannelBotAuthorizations { get; }
    DbSet<BotAccount> BotAccounts { get; }
    DbSet<AuthSession> AuthSessions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<IpcDevModeKey> IpcDevModeKeys { get; }
    DbSet<IntegrationConnection> IntegrationConnections { get; }
    DbSet<IntegrationToken> IntegrationTokens { get; }
    DbSet<DiscordServerAuthorization> DiscordServerAuthorizations { get; }
    DbSet<ChannelSubscription> ChannelSubscriptions { get; }
    DbSet<TtsVoice> TtsVoices { get; }
    DbSet<UserTtsVoice> UserTtsVoices { get; }
    DbSet<TtsUsageRecord> TtsUsageRecords { get; }
    DbSet<TtsCacheEntry> TtsCacheEntries { get; }
    DbSet<Pronoun> Pronouns { get; }
    DbSet<DeletionAuditLog> DeletionAuditLogs { get; }
    DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers { get; }
    DbSet<EventResponse> EventResponses { get; }
    DbSet<WatchStreak> WatchStreaks { get; }
    DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines { get; }

    // Event store (append-only journal + per-tenant sequences + projection checkpoints)
    DbSet<EventJournal> EventJournals { get; }
    DbSet<TenantSequence> TenantSequences { get; }
    DbSet<ProjectionCheckpoint> ProjectionCheckpoints { get; }

    // Roles & permissions (Plane A/B) — the authorization data the gates read.
    DbSet<ChannelMembership> ChannelMemberships { get; }
    DbSet<ChannelCommunityStanding> ChannelCommunityStandings { get; }
    DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions { get; }
    DbSet<ChannelActionOverride> ChannelActionOverrides { get; }
    DbSet<PermitGrant> PermitGrants { get; }

    // Platform IAM (Plane C) — SaaS operator/admin access control. Empty on self-host (owner = full).
    DbSet<IamPermission> IamPermissions { get; }
    DbSet<IamRole> IamRoles { get; }
    DbSet<IamRolePermission> IamRolePermissions { get; }
    DbSet<IamPrincipal> IamPrincipals { get; }
    DbSet<IamRoleAssignment> IamRoleAssignments { get; }
    DbSet<IamAuditLog> IamAuditLogs { get; }

    // Economy — currency core (economy.md K.1-K.3) + catalog (K.10-K.11).
    DbSet<CurrencyConfig> CurrencyConfigs { get; }
    DbSet<EarningRule> EarningRules { get; }
    DbSet<CurrencyAccount> CurrencyAccounts { get; }
    DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries { get; }
    DbSet<CatalogItem> CatalogItems { get; }
    DbSet<CatalogPurchase> CatalogPurchases { get; }
    DbSet<GameConfig> GameConfigs { get; }
    DbSet<GamePlay> GamePlays { get; }
    DbSet<ViewerAgeConsent> ViewerAgeConsents { get; }
    DbSet<SavingsJar> SavingsJars { get; }
    DbSet<SavingsJarMembership> SavingsJarMemberships { get; }
    DbSet<JarContribution> JarContributions { get; }
    DbSet<LeaderboardConfig> LeaderboardConfigs { get; }
    DbSet<LeaderboardOptOut> LeaderboardOptOuts { get; }
    DbSet<LeaderboardSnapshot> LeaderboardSnapshots { get; }
    DbSet<BillingTier> BillingTiers { get; }
    DbSet<TierLimit> TierLimits { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<UsageRecord> UsageRecords { get; }
    DbSet<FoundersBadge> FoundersBadges { get; }
    DbSet<InviteCode> InviteCodes { get; }
    DbSet<FederationPeer> FederationPeers { get; }
    DbSet<FederationPeerKey> FederationPeerKeys { get; }
    DbSet<ChannelFederationOptIn> ChannelFederationOptIns { get; }
    DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints { get; }
    DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries { get; }
    DbSet<InboundWebhookEndpoint> InboundWebhookEndpoints { get; }
    DbSet<HttpEgressAllowlist> HttpEgressAllowlists { get; }
    DbSet<ViewerProfile> ViewerProfiles { get; }
    DbSet<WatchSession> WatchSessions { get; }
    DbSet<MessageActivityDaily> MessageActivityDailies { get; }
    DbSet<ViewerEngagementDaily> ViewerEngagementDailies { get; }
    DbSet<ChannelAnalyticsDaily> ChannelAnalyticsDailies { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
