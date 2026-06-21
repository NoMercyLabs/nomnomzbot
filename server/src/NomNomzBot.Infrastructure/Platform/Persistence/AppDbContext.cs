// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentTenantService? _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    // Read by the named tenant query filter at query time (schema §1.2). Null tenant ⇒ no tenant
    // predicate (background / cross-tenant reads see all rows; soft-delete still applies).
    private Guid? CurrentBroadcasterId => _currentTenant?.BroadcasterId;

    // Core
    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();

    // Bot features
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Widget> Widgets => Set<Widget>();

    // EventSub — per-tenant subscription registry (F.7), app-global conduit + shards (F.8/F.9),
    // and the scoped idempotency markers the notification dispatcher dedupes on (O.4).
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // Chat
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<global::NomNomzBot.Domain.Stream.Entities.Stream>();

    // Config & Storage
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
    public DbSet<Storage> Storages => Set<Storage>();
    public DbSet<Record> Records => Set<Record>();

    // Permissions
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ChannelFeature> ChannelFeatures => Set<ChannelFeature>();

    // Auth & Billing
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        Set<ChannelBotAuthorization>();
    public DbSet<BotAccount> BotAccounts => Set<BotAccount>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => Set<IpcDevModeKey>();
    public DbSet<DiscordServerAuthorization> DiscordServerAuthorizations =>
        Set<DiscordServerAuthorization>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();

    // Integrations (token vault — identity-auth Domain E)
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();

    // TTS
    public DbSet<TtsVoice> TtsVoices => Set<TtsVoice>();
    public DbSet<UserTtsVoice> UserTtsVoices => Set<UserTtsVoice>();
    public DbSet<TtsUsageRecord> TtsUsageRecords => Set<TtsUsageRecord>();
    public DbSet<TtsCacheEntry> TtsCacheEntries => Set<TtsCacheEntry>();

    // Reference data
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();

    // Audit
    public DbSet<DeletionAuditLog> DeletionAuditLogs => Set<DeletionAuditLog>();

    // Timers
    public DbSet<Domain.Commands.Entities.Timer> Timers => Set<Domain.Commands.Entities.Timer>();

    // Event responses
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();

    // Watch streaks
    public DbSet<WatchStreak> WatchStreaks => Set<WatchStreak>();

    // Pipelines
    public DbSet<Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<Domain.Commands.Entities.Pipeline>();

    // Event store — append-only journal (O.1), per-tenant sequences (Q.3), projection checkpoints (O.3)
    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();

    // Roles & permissions (Plane A/B)
    public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<ChannelCommunityStanding>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        Set<NomNomzBot.Domain.Identity.Entities.ActionDefinition>();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => Set<ChannelActionOverride>();
    public DbSet<PermitGrant> PermitGrants => Set<PermitGrant>();

    // Platform IAM (Plane C)
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Composing tenant + soft-delete global query filters (schema §1.2). Applied after the
        // per-entity configurations so it is the single authoritative filter per entity; the
        // configurations themselves no longer call HasQueryFilter.
        modelBuilder.ApplyTenantAndSoftDeleteFilters(() => CurrentBroadcasterId);
    }
}
