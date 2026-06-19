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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    // Core
    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();

    // Bot features
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();

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
    public DbSet<DiscordServerAuthorization> DiscordServerAuthorizations =>
        Set<DiscordServerAuthorization>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
