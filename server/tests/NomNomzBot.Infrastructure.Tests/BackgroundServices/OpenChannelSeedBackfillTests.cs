// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Proves the startup backfill seeds an OPEN channel the way it seeds an onboarded one: a channel that did not
/// onboard, but that an onboarded user moderates, gets the default commands and event responses through the
/// real seed handlers; a second run adds nothing; a channel nobody onboarded moderates stays unseeded.
/// </summary>
public sealed class OpenChannelSeedBackfillTests
{
    private static readonly string[] DefaultCommandKeys =
    [
        "sr",
        "skip",
        "queue",
        "volume",
        "song",
        "banger",
        "pause",
        "resume",
    ];

    private static readonly Guid Moderator = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid ModeratorHome = Guid.Parse("0192a000-0000-7000-8000-00000000e002");
    private static readonly Guid OpenChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e003");
    private static readonly Guid StrangerUser = Guid.Parse("0192a000-0000-7000-8000-00000000e004");
    private static readonly Guid ClosedChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e005");

    [Fact]
    public async Task Open_non_onboarded_channel_gets_default_commands_and_event_responses()
    {
        AuthDbContext db = await BuildWorldAsync();

        await RunBackfillAsync(db);

        List<ChannelBuiltinCommand> commands = await db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == OpenChannel)
            .ToListAsync();
        commands.Select(c => c.BuiltinKey).Should().BeEquivalentTo(DefaultCommandKeys);
        commands.Should().OnlyContain(c => c.IsEnabled);

        List<EventResponse> responses = await db
            .EventResponses.Where(e => e.BroadcasterId == OpenChannel)
            .ToListAsync();
        responses
            .Select(e => e.EventType)
            .Should()
            .BeEquivalentTo(EventResponsePresetCatalog.EventTypes);
        // The raid and cheer rows are wired to their own welcome pipelines by the same seed handler.
        responses
            .Where(e => e.EventType is not ("channel.raid" or "channel.cheer"))
            .Should()
            .OnlyContain(e =>
                !e.IsEnabled && e.ResponseType == "chat_message" && e.FollowsPlatformDefault
            );
        EventResponse raid = responses.Single(e => e.EventType == "channel.raid");
        raid.IsEnabled.Should().BeTrue();
        raid.ResponseType.Should().Be("pipeline");
        raid.PipelineId.Should().NotBeNull();
    }

    [Fact]
    public async Task Second_run_adds_no_rows()
    {
        AuthDbContext db = await BuildWorldAsync();
        await RunBackfillAsync(db);
        int commandsBefore = await db.ChannelBuiltinCommands.CountAsync();
        int responsesBefore = await db.EventResponses.CountAsync();
        (await db.ChannelBuiltinCommands.CountAsync(c => c.BroadcasterId == OpenChannel))
            .Should()
            .Be(DefaultCommandKeys.Length);

        await RunBackfillAsync(db);

        (await db.ChannelBuiltinCommands.CountAsync()).Should().Be(commandsBefore);
        (await db.EventResponses.CountAsync()).Should().Be(responsesBefore);
    }

    [Fact]
    public async Task Non_onboarded_channel_nobody_onboarded_moderates_gets_no_rows()
    {
        AuthDbContext db = await BuildWorldAsync();

        await RunBackfillAsync(db);

        (await db.ChannelBuiltinCommands.CountAsync(c => c.BroadcasterId == ClosedChannel))
            .Should()
            .Be(0);
        (await db.EventResponses.CountAsync(e => e.BroadcasterId == ClosedChannel)).Should().Be(0);
    }

    private static async Task<AuthDbContext> BuildWorldAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(ModeratorHome, Moderator, "tw-home", "homechan", true));
        db.Channels.Add(Channel(OpenChannel, Guid.NewGuid(), "tw-open", "openchan", false));
        db.Channels.Add(Channel(ClosedChannel, Guid.NewGuid(), "tw-closed", "closedchan", false));
        db.ChannelMemberships.Add(Membership(OpenChannel, Moderator));
        db.ChannelMemberships.Add(Membership(ClosedChannel, StrangerUser));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task RunBackfillAsync(AuthDbContext db)
    {
        OnboardedChannelSeedBackfillService sut = new(
            new SingleContextScopeFactory(db),
            new SeedingEventBus(db),
            NullLogger<OnboardedChannelSeedBackfillService>.Instance
        );

        await sut.StartAsync(CancellationToken.None);
        if (sut.ExecuteTask is not null)
            await sut.ExecuteTask;
        await sut.StopAsync(CancellationToken.None);
    }

    private static Channel Channel(
        Guid id,
        Guid ownerUserId,
        string twitchId,
        string name,
        bool isOnboarded
    ) =>
        new()
        {
            Id = id,
            OwnerUserId = ownerUserId,
            TwitchChannelId = twitchId,
            Name = name,
            NameNormalized = name,
            IsOnboarded = isOnboarded,
        };

    private static ChannelMembership Membership(Guid broadcasterId, Guid userId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            UserId = userId,
            ManagementRole = ManagementRole.Moderator,
            LevelValue = ManagementRole.Moderator.ToLevel(),
            Source = MembershipSource.TwitchBadge,
            GrantedAt = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc),
        };

    /// <summary>An event bus that hands each onboarding event to the real default-commands and event-response seed handlers.</summary>
    private sealed class SeedingEventBus(AuthDbContext db) : IEventBus
    {
        public async Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            if (@event is not ChannelOnboardedEvent onboarded)
                return;

            await new DefaultCommandsSeedOnOnboardingHandler(
                new DefaultCommandsSeeder(db),
                NullLogger<DefaultCommandsSeedOnOnboardingHandler>.Instance
            ).HandleAsync(onboarded, cancellationToken);
            await new EventResponseDefaultsSeedOnOnboardingHandler(
                new EventResponseDefaultsSeeder(db),
                new RaidResponseFlowSeeder(db),
                new CheerResponseFlowSeeder(db),
                NullLogger<EventResponseDefaultsSeedOnOnboardingHandler>.Instance
            ).HandleAsync(onboarded, cancellationToken);
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => PublishAsync(@event).GetAwaiter().GetResult();
    }

    private sealed class SingleContextScopeFactory(IApplicationDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db);

        private sealed class Scope(IApplicationDbContext db) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IApplicationDbContext) ? db : null;

            public void Dispose() { }
        }
    }
}
