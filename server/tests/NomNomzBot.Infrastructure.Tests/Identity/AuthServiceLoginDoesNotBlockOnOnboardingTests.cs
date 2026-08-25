// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Signing in took 13-19 SECONDS on the live box, every time. The cause was not the token exchange: the
/// callback awaited <see cref="ChannelOnboardedEvent"/>, whose handler set is a whole repair sweep —
/// rewards, moderator roster, memberships, standings, channel info, banned-user import, bot mod-join,
/// default commands, and an EventSub subscribe of ~74 topics where every failing one costs a round-trip.
/// None of that is needed to hand the operator their tokens, so the login now returns immediately and the
/// sweep runs behind it.
/// </summary>
public sealed class AuthServiceLoginDoesNotBlockOnOnboardingTests
{
    // Must match the identity the shared FakeTwitchHelixHandler returns, or the lookup misses the seeded
    // channel and the service onboards a brand-new one instead.
    private const string TwitchUserId = "tw-100";

    /// <summary>Stands in for the real handler set being slow. Anything that AWAITS the onboarding publish
    /// pays this cost on the login path; anything that fires it forgetfully does not.</summary>
    private sealed class SlowOnboardingEventBus : IEventBus
    {
        public static readonly TimeSpan HandlerCost = TimeSpan.FromSeconds(5);

        public List<IDomainEvent> Published { get; } = [];

        public async Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            if (@event is ChannelOnboardedEvent)
                await Task.Delay(HandlerCost, cancellationToken);
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }

    [Fact]
    public async Task Signing_in_does_not_wait_for_the_channel_repair_sweep()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
        Guid channelId = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
        db.Users.Add(
            new()
            {
                Id = ownerId,
                TwitchUserId = TwitchUserId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = TwitchUserId,
                Name = "stoney",
                NameNormalized = "stoney",
                IsOnboarded = true,
            }
        );

        db.SaveChanges();

        SlowOnboardingEventBus bus = new();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(db, bus);

        Stopwatch clock = Stopwatch.StartNew();
        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );
        clock.Stop();

        result.IsSuccess.Should().BeTrue();

        // The sweep still happens — it is the repair path that keeps a re-authed channel in sync.
        bus.Published.OfType<ChannelOnboardedEvent>()
            .Should()
            .ContainSingle(e => e.BroadcasterId == channelId);

        // ...but the operator does not sit through it. Awaiting the publish would put the whole handler
        // cost on this stopwatch, which is exactly the 13-19s login observed live.
        clock
            .Elapsed.Should()
            .BeLessThan(
                SlowOnboardingEventBus.HandlerCost,
                "the login must return as soon as the session exists, not after the repair sweep finishes"
            );
    }
}
