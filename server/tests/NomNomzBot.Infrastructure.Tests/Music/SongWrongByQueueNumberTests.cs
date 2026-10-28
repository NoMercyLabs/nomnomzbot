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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Live 2026-10-16: <c>!queue</c> shows "#5 Title by Artist" with no song code, so a viewer could not remove
/// their own song by the number they were shown. <c>!wrongsong N</c> removes the entry at queue number N —
/// the same numbering <c>!queue</c> prints — but only when the caller requested it.
/// </summary>
public sealed class SongWrongByQueueNumberTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab07");

    private static readonly ActionDefinition Definition = new() { Type = "song_wrong" };

    private static MusicQueue QueueOfThree() =>
        new(
            null,
            [
                new("My Pick", "A", null, 100, "Bamo", 0, "K7QM"),
                new("Their Pick", "B", null, 100, "SomeoneElse", 0, "P4XT"),
                new("My Newest", "C", null, 100, "Bamo", 0, "C3DE"),
            ]
        );

    private static (SongWrongAction Action, IMusicService Music, IChatProvider Chat) Build()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(QueueOfThree());
        music
            .RetractRequestAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RequestRetraction.Removed);
        IChatProvider chat = Substitute.For<IChatProvider>();
        SongWrongAction action = new(
            music,
            MusicTestDbContext.New(),
            chat,
            TestBuiltinComposer.Create(),
            NullLogger<SongWrongAction>.Instance
        );
        return (action, music, chat);
    }

    private static PipelineExecutionContext Ctx(string number)
    {
        PipelineExecutionContext context = new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!wrongsong " + number,
        };
        context.Variables["args.1"] = number;
        return context;
    }

    [Fact]
    public async Task Own_request_at_the_queue_number_is_removed_through_the_spotify_aware_path()
    {
        (SongWrongAction action, IMusicService music, _) = Build();

        ActionResult result = await action.ExecuteAsync(Ctx("1"), Definition);

        // Number 1, not the caller's newest (#3): the fallback would have picked the wrong song.
        result.Output.Should().Be("removed: My Pick");
        await music
            .Received(1)
            .RetractRequestAsync(ChannelId.ToString(), 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_queue_number_is_refused_and_nothing_is_removed()
    {
        (SongWrongAction action, IMusicService music, IChatProvider chat) = Build();

        await action.ExecuteAsync(Ctx("2"), Definition);

        await music.DidNotReceiveWithAnyArgs().RetractRequestAsync(default!, default);
        await chat.Received(1)
            .SendMessageAsync(
                ChannelId,
                "You can only skip songs you requested yourself.",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_queue_number_past_the_end_is_refused_and_nothing_is_removed()
    {
        (SongWrongAction action, IMusicService music, IChatProvider chat) = Build();

        await action.ExecuteAsync(Ctx("9"), Definition);

        await music.DidNotReceiveWithAnyArgs().RetractRequestAsync(default!, default);
        await chat.Received(1)
            .SendMessageAsync(
                ChannelId,
                "@Bamo You don't have a request at position 9.",
                Arg.Any<CancellationToken>()
            );
    }
}
