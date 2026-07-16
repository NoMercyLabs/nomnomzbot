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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community;

/// <summary>
/// Proves the bot-run chat poll loop: opening announces the question + numbered options in chat and
/// arms the hot-path cache; only one poll is open per channel; votes tally live with a viewer's LAST
/// vote winning (cross-platform voters are distinct); a vote after the auto-close horizon closes the
/// poll instead of counting; closing announces the winner and keeps the poll as history.
/// </summary>
public sealed class ChatPollServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f7f00-7777-7000-8000-000000000001");
    private static readonly DateTimeOffset Start = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static (
        ChatPollService Service,
        IChatProvider Chat,
        ChannelContext Ctx,
        FakeTimeProvider Time
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChannelContext ctx = new()
        {
            BroadcasterId = Tenant,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Tenant).Returns(ctx);
        IChatProvider chat = Substitute.For<IChatProvider>();
        FakeTimeProvider time = new(Start);
        ChatPollService service = new(
            db,
            registry,
            chat,
            time,
            NullLogger<ChatPollService>.Instance
        );
        return (service, chat, ctx, time);
    }

    private static Task<Result<ChatPollDto>> OpenAsync(
        ChatPollService service,
        int? durationSeconds = null
    ) =>
        service.OpenAsync(
            Tenant.ToString(),
            new OpenChatPollRequest
            {
                Question = "Next game?",
                Options = ["Factorio", "Peak", "Satisfactory"],
                DurationSeconds = durationSeconds,
            }
        );

    [Fact]
    public async Task Opening_announces_the_numbered_options_and_arms_the_hot_path()
    {
        (ChatPollService service, IChatProvider chat, ChannelContext ctx, _) = Build();

        Result<ChatPollDto> opened = await OpenAsync(service);

        opened.IsSuccess.Should().BeTrue();
        opened
            .Value.Options.Select(o => o.Label)
            .Should()
            .Equal("Factorio", "Peak", "Satisfactory");
        await chat.Received(1)
            .SendMessageAsync(
                Tenant,
                Arg.Is<string>(m =>
                    m.Contains("Next game?")
                    && m.Contains("1: Factorio")
                    && m.Contains("3: Satisfactory")
                ),
                Arg.Any<CancellationToken>()
            );
        ctx.ActiveChatPoll.Should().NotBeNull();
        ctx.ActiveChatPoll!.OptionCount.Should().Be(3);
    }

    [Fact]
    public async Task Only_one_poll_is_open_per_channel()
    {
        (ChatPollService service, _, _, _) = Build();
        await OpenAsync(service);

        Result<ChatPollDto> second = await OpenAsync(service);

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("CONFLICT");
    }

    [Fact]
    public async Task Votes_tally_live_and_a_viewers_last_vote_wins()
    {
        (ChatPollService service, _, _, _) = Build();
        Guid pollId = (await OpenAsync(service)).Value.Id;

        await service.RecordVoteAsync(Tenant, pollId, "twitch", "alice", 1);
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "bob", 2);
        await service.RecordVoteAsync(Tenant, pollId, "youtube", "alice", 2); // distinct voter
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "alice", 3); // changed her mind

        ChatPollDto poll = (await service.GetAsync(Tenant.ToString(), pollId)).Value;
        poll.TotalVotes.Should().Be(3, "three distinct voters, re-votes replace");
        poll.Options.Single(o => o.Index == 1).Votes.Should().Be(0);
        poll.Options.Single(o => o.Index == 2).Votes.Should().Be(2);
        poll.Options.Single(o => o.Index == 3).Votes.Should().Be(1);
    }

    [Fact]
    public async Task A_vote_after_the_horizon_closes_the_poll_instead_of_counting()
    {
        (ChatPollService service, IChatProvider chat, ChannelContext ctx, FakeTimeProvider time) =
            Build();
        Guid pollId = (await OpenAsync(service, durationSeconds: 60)).Value.Id;
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "alice", 1);
        chat.ClearReceivedCalls();

        time.Advance(TimeSpan.FromSeconds(61));
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "bob", 2);

        ChatPollDto poll = (await service.GetAsync(Tenant.ToString(), pollId)).Value;
        poll.Status.Should().Be("closed");
        poll.TotalVotes.Should().Be(1, "the late vote must not count");
        ctx.ActiveChatPoll.Should().BeNull("the hot path is disarmed on close");
        await chat.Received(1)
            .SendMessageAsync(
                Tenant,
                Arg.Is<string>(m => m.Contains("POLL CLOSED") && m.Contains("Factorio")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Closing_announces_the_winner_and_keeps_the_poll_as_history()
    {
        (ChatPollService service, IChatProvider chat, ChannelContext ctx, _) = Build();
        Guid pollId = (await OpenAsync(service)).Value.Id;
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "alice", 2);
        await service.RecordVoteAsync(Tenant, pollId, "twitch", "bob", 2);
        chat.ClearReceivedCalls();

        Result<ChatPollDto> closed = await service.CloseAsync(Tenant.ToString(), pollId);

        closed.Value.Status.Should().Be("closed");
        await chat.Received(1)
            .SendMessageAsync(
                Tenant,
                Arg.Is<string>(m => m.Contains("Peak") && m.Contains("2/2")),
                Arg.Any<CancellationToken>()
            );
        ctx.ActiveChatPoll.Should().BeNull();
        (await service.CloseAsync(Tenant.ToString(), pollId)).IsFailure.Should().BeTrue();
        (await service.ListAsync(Tenant.ToString()))
            .Value.Should()
            .ContainSingle(p => p.Id == pollId);
    }

    [Fact]
    public async Task A_poll_needs_at_least_two_real_options()
    {
        (ChatPollService service, _, _, _) = Build();

        Result<ChatPollDto> opened = await service.OpenAsync(
            Tenant.ToString(),
            new OpenChatPollRequest { Question = "hm?", Options = ["only", "   "] }
        );

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
