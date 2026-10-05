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
using NomNomzBot.Application.Abstractions.Persistence;
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
        ITwitchModerationApi moderation,
        IApplicationDbContext? db = null
    ) =>
        new(
            new OperatorModeratedChannelResolver(
                access,
                moderators,
                db ?? ModerationServiceTestDbContext.New()
            ),
            moderation,
            NullLogger<OperatorNetworkBanService>.Instance
        );

    private static readonly Guid OwnChannel = Guid.NewGuid();

    /// <summary>The operator owns "stoney" (Twitch id own1) and moderates alpha (b1) and bravo (b2).</summary>
    private static (
        IChannelAccessService Access,
        ITwitchModeratorsApi Moderators,
        IApplicationDbContext Db
    ) OwnerWhoModeratesTwoChannels()
    {
        IChannelAccessService access = Substitute.For<IChannelAccessService>();
        access
            .ResolveOwnChannelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OwnChannel);

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
                        [new("b1", "alpha", "Alpha"), new("b2", "bravo", "Bravo")],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = OwnChannel,
                TwitchChannelId = "own1",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();
        return (access, moderators, db);
    }

    private static TwitchBlockedTerm Term(string broadcasterId, string id, string text) =>
        new(
            broadcasterId,
            "mod",
            id,
            text,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null
        );

    [Fact]
    public async Task Blocks_a_term_in_the_operators_own_channel_and_every_channel_they_moderate()
    {
        (IChannelAccessService access, ITwitchModeratorsApi moderators, IApplicationDbContext db) =
            OwnerWhoModeratesTwoChannels();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .AddBlockedTermAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(Term(call.ArgAt<string>(1), "t", "twitchstar*")));
        moderation
            .AddBlockedTermAsOperatorAsync(
                Arg.Any<Guid>(),
                "b2",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBlockedTerm>("Missing scope.", "FORBIDDEN"));

        Result<NetworkBanResult> result = await Build(access, moderators, moderation, db)
            .BlockTermAcrossModeratedAsync(Operator, "  twitchstar*  ");

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Attempted.Should()
            .Be(3, "the operator's own channel is one they moderate too");
        result.Value.Succeeded.Should().Be(2);
        result
            .Value.Channels.Select(c => c.BroadcasterLogin)
            .Should()
            .BeEquivalentTo(["stoney", "alpha", "bravo"]);
        result
            .Value.Channels.Single(c => c.BroadcasterLogin == "bravo")
            .Error.Should()
            .Be("Missing scope.");
        await moderation
            .Received(1)
            .AddBlockedTermAsOperatorAsync(
                Operator,
                "own1",
                "twitchstar*",
                Arg.Any<CancellationToken>()
            );
        await moderation
            .Received(1)
            .AddBlockedTermAsOperatorAsync(
                Operator,
                "b1",
                "twitchstar*",
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("x")]
    [InlineData(" ")]
    public async Task A_term_Twitch_would_reject_is_refused_before_any_channel_is_touched(
        string text
    )
    {
        (IChannelAccessService access, ITwitchModeratorsApi moderators, IApplicationDbContext db) =
            OwnerWhoModeratesTwoChannels();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();

        Result<NetworkBanResult> result = await Build(access, moderators, moderation, db)
            .BlockTermAcrossModeratedAsync(Operator, text);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await moderation
            .DidNotReceiveWithAnyArgs()
            .AddBlockedTermAsOperatorAsync(default, default!, default!);
    }

    [Fact]
    public async Task Unblocking_a_term_removes_its_own_id_in_each_channel_and_passes_channels_without_it()
    {
        (IChannelAccessService access, ITwitchModeratorsApi moderators, IApplicationDbContext db) =
            OwnerWhoModeratesTwoChannels();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBlockedTermsAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchPage<TwitchBlockedTerm>([], null, 0)));
        // alpha holds the term on its second page, in a different case; the operator's own channel on page one.
        moderation
            .GetBlockedTermsAsOperatorAsync(
                Arg.Any<Guid>(),
                "b1",
                Arg.Is<TwitchPageRequest>(p => p.After == null),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>([Term("b1", "x1", "other")], "page2", 0)
                )
            );
        moderation
            .GetBlockedTermsAsOperatorAsync(
                Arg.Any<Guid>(),
                "b1",
                Arg.Is<TwitchPageRequest>(p => p.After == "page2"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>([Term("b1", "a-id", "TwitchStar*")], null, 0)
                )
            );
        moderation
            .GetBlockedTermsAsOperatorAsync(
                Arg.Any<Guid>(),
                "own1",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>(
                        [Term("own1", "o-id", "twitchstar*")],
                        null,
                        0
                    )
                )
            );
        moderation
            .RemoveBlockedTermAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        Result<NetworkBanResult> result = await Build(access, moderators, moderation, db)
            .UnblockTermAcrossModeratedAsync(Operator, "twitchstar*");

        result.IsSuccess.Should().BeTrue();
        result.Value.Attempted.Should().Be(3);
        result.Value.Succeeded.Should().Be(3, "a channel that never had the term is already clean");
        await moderation
            .Received(1)
            .RemoveBlockedTermAsOperatorAsync(Operator, "b1", "a-id", Arg.Any<CancellationToken>());
        await moderation
            .Received(1)
            .RemoveBlockedTermAsOperatorAsync(
                Operator,
                "own1",
                "o-id",
                Arg.Any<CancellationToken>()
            );
        await moderation
            .DidNotReceive()
            .RemoveBlockedTermAsOperatorAsync(
                Arg.Any<Guid>(),
                "b2",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

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

    [Fact]
    public async Task Unbans_every_moderated_channel_as_the_operator_and_reports_per_channel_outcomes()
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
        // Default: every unban succeeds; then override the middle channel to fail.
        moderation
            .UnbanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        moderation
            .UnbanAsOperatorAsync(
                Arg.Any<Guid>(),
                "b2",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("You do not moderate that channel.", "FORBIDDEN"));

        Result<NetworkBanResult> result = await Build(access, moderators, moderation)
            .UnbanAcrossModeratedAsync(Operator, "target-99");

        result.IsSuccess.Should().BeTrue();
        result.Value.Attempted.Should().Be(3);
        result.Value.Succeeded.Should().Be(2, "the failed channel did not abort the sweep");
        result.Value.Channels.Should().HaveCount(3);

        ChannelBanOutcome bravo = result.Value.Channels.Single(c => c.BroadcasterLogin == "bravo");
        bravo.Succeeded.Should().BeFalse();
        bravo.Error.Should().Be("You do not moderate that channel.");

        // Each channel was actually unbanned, as the operator, with the given target.
        await moderation
            .Received(1)
            .UnbanAsOperatorAsync(Operator, "b1", "target-99", Arg.Any<CancellationToken>());
        await moderation
            .Received(1)
            .UnbanAsOperatorAsync(Operator, "b3", "target-99", Arg.Any<CancellationToken>());
    }
}
