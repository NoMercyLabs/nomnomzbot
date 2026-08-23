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
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves <see cref="OperatorMessageDeleter"/> deletes AS THE LOGGED-IN OPERATOR (chat-client.md §3.5): it resolves
/// the tenant Guid to the channel's raw Twitch id and delegates to
/// <see cref="ITwitchModerationApi.DeleteChatMessageAsOperatorAsync"/> (which rides the operator's own token and sets
/// the operator as <c>moderator_id</c>) — never a Guid to Twitch. An unknown channel fails cleanly with no Twitch
/// call, and the operator's <c>no_token</c> (no linked Twitch identity) propagates so the caller can fall back.
/// S013: a successful delete also leaves an audit trail naming the acting operator (moderation.md's mod-log
/// discipline extended to message deletion) — recorded only once Twitch actually deleted it, never on failure.
/// </summary>
public sealed class OperatorMessageDeleterTests
{
    private const string ActionRecordType = "chat_action";
    private static readonly Guid Operator = Guid.Parse("0197b2c0-0000-7000-8000-0000000000c3");
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000d4");

    [Fact]
    public async Task DeleteAsUserAsync_deletes_as_the_operator_against_the_resolved_channel_twitch_id()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsOperatorAsync(
                Operator,
                "channel-777",
                "msg-9",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns("channel-777");

        OperatorMessageDeleter deleter = new(
            moderation,
            identity,
            db,
            NullLogger<OperatorMessageDeleter>.Instance
        );

        Result result = await deleter.DeleteAsUserAsync(Operator, Broadcaster, "msg-9");

        result.IsSuccess.Should().BeTrue();
        // Delegates with the operator's user id + the RESOLVED raw Twitch channel id — never a tenant Guid to Twitch.
        await moderation
            .Received(1)
            .DeleteChatMessageAsOperatorAsync(
                Operator,
                "channel-777",
                "msg-9",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeleteAsUserAsync_WhenTwitchApplies_RecordsAnAuditRowNamingTheOperator()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsOperatorAsync(
                Operator,
                "channel-777",
                "msg-9",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns("channel-777");

        OperatorMessageDeleter deleter = new(
            moderation,
            identity,
            db,
            NullLogger<OperatorMessageDeleter>.Instance
        );

        Result result = await deleter.DeleteAsUserAsync(Operator, Broadcaster, "msg-9");
        result.IsSuccess.Should().BeTrue();

        List<RecordEntity> rows = await db
            .Records.Where(r => r.RecordType == ActionRecordType)
            .ToListAsync();
        rows.Should().ContainSingle();
        RecordEntity row = rows.Single();
        row.BroadcasterId.Should().Be(Broadcaster);
        row.UserId.Should().Be(Operator.ToString());
        row.Data.Should().Contain("msg-9");
    }

    [Fact]
    public async Task DeleteAsUserAsync_fails_not_found_and_never_calls_twitch_when_the_channel_is_unknown()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        OperatorMessageDeleter deleter = new(
            moderation,
            identity,
            db,
            NullLogger<OperatorMessageDeleter>.Instance
        );

        Result result = await deleter.DeleteAsUserAsync(Operator, Broadcaster, "msg-9");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        await moderation
            .DidNotReceive()
            .DeleteChatMessageAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeleteAsUserAsync_propagates_no_token_so_the_caller_can_fall_back_to_the_tenant_delete_and_records_nothing()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure(
                    "You have no linked Twitch identity to moderate as.",
                    TwitchErrorCodes.NoToken
                )
            );
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns("channel-777");

        OperatorMessageDeleter deleter = new(
            moderation,
            identity,
            db,
            NullLogger<OperatorMessageDeleter>.Instance
        );

        Result result = await deleter.DeleteAsUserAsync(Operator, Broadcaster, "msg-9");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NoToken);
        // A rejected delete never gets a fake audit row — the same enforce-then-record discipline as ban/timeout.
        (await db.Records.CountAsync())
            .Should()
            .Be(0);
    }
}
