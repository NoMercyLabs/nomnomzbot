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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the operator network-ban fan-out (chat-client.md §3.5): it resolves the operator's moderated channels from
/// Twitch and bans the target in each AS THE OPERATOR, best-effort — a channel that fails is recorded and the sweep
/// continues, the per-channel outcomes and success tally are reported, an operator who owns no channel bans nowhere,
/// and a failure to even list the moderated channels surfaces as a failure (not a silent empty success).
/// </summary>
public sealed class OperatorNetworkBanServiceTests
{
    private static readonly Guid Operator = Guid.NewGuid();

    private static OperatorNetworkBanService Build(
        IChannelAccessService access,
        ITwitchModeratorsApi moderators,
        ITwitchModerationApi moderation
    ) => new(access, moderators, moderation, NullLogger<OperatorNetworkBanService>.Instance);

    [Fact]
    public async Task Bans_every_moderated_channel_as_the_operator_and_reports_per_channel_outcomes()
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratedChannelsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [
                            new("b1", "alpha", "Alpha"),
                            new("b2", "bravo", "Bravo"),
                            new("b3", "charlie", "Charlie"),
                        ],
                        NextCursor: null,
                        Total: 3
                    )
                )
            );

        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        // Default: every ban succeeds; then override the middle channel to fail.
        moderation
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<TwitchBanResult>(null!));
        moderation
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                "b2",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>("You are banned in that channel.", "FORBIDDEN")
            );

        Result<NetworkBanResult> result = await Build(access, moderators, moderation)
            .BanAcrossModeratedAsync(Operator, "target-99", "spam");

        result.IsSuccess.Should().BeTrue();
        result.Value.Attempted.Should().Be(3);
        result.Value.Succeeded.Should().Be(2, "the failed channel did not abort the sweep");
        result.Value.Channels.Should().HaveCount(3);

        ChannelBanOutcome bravo = result.Value.Channels.Single(c => c.BroadcasterLogin == "bravo");
        bravo.Succeeded.Should().BeFalse();
        bravo.Error.Should().Be("You are banned in that channel.");
        result
            .Value.Channels.Where(c => c.BroadcasterLogin != "bravo")
            .Should()
            .OnlyContain(c => c.Succeeded);

        // Each channel was actually banned, as the operator, with the given target + reason.
        await moderation
            .Received(1)
            .BanAsOperatorAsync(Operator, "b1", "target-99", "spam", Arg.Any<CancellationToken>());
        await moderation
            .Received(1)
            .BanAsOperatorAsync(Operator, "b3", "target-99", "spam", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_operator_who_owns_no_channel_moderates_nothing()
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Guid.Empty);

        Result<NetworkBanResult> result = await Build(
                access,
                Substitute.For<ITwitchModeratorsApi>(),
                Substitute.For<ITwitchModerationApi>()
            )
            .BanAcrossModeratedAsync(Operator, "target-99", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Attempted.Should().Be(0);
        result.Value.Channels.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failure_listing_moderated_channels_surfaces_as_a_failure()
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());

        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratedChannelsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchModeratedChannel>>(
                    "Missing required scope 'user:read:moderated_channels'.",
                    "MISSING_SCOPE"
                )
            );

        Result<NetworkBanResult> result = await Build(
                access,
                moderators,
                Substitute.For<ITwitchModerationApi>()
            )
            .BanAcrossModeratedAsync(Operator, "target-99", null);

        result
            .IsFailure.Should()
            .BeTrue("we must not silently report zero bans when we could not even list channels");
    }
}
