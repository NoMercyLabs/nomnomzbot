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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Services.Viewer;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Viewer;

/// <summary>A viewer sees only their own songs in a channel's queue, each with its real place in line.</summary>
public sealed class ViewerSelfQueueServiceTests
{
    private static readonly Guid Caller = Guid.Parse("0192a000-0000-7000-8000-0000000006a1");
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000006c1");

    [Fact]
    public async Task Returns_only_the_callers_songs_with_their_one_based_place_in_line()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = Caller,
                TwitchUserId = "tw-6a1",
                Username = "viewerone",
                UsernameNormalized = "viewerone",
                DisplayName = "ViewerOne",
            }
        );
        await db.SaveChangesAsync();

        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new MusicQueue(
                    null,
                    [
                        new("Other A", "Artist", null, 1000, "someone"),
                        new(
                            "Mine A",
                            "Artist",
                            "https://img/a.png",
                            2000,
                            "viewerone",
                            Code: "K7QM"
                        ),
                        new("Other B", "Artist", null, 1000, "else"),
                        new("Mine B", "Artist", null, 3000, "VIEWERONE"),
                    ]
                )
            );

        ViewerSelfQueueService sut = new(db, music);

        Result<ViewerQueueDto> result = await sut.GetMyQueueAsync(Channel, Caller);

        result.IsSuccess.Should().BeTrue();
        result.Value.QueueLength.Should().Be(4);
        result
            .Value.Songs.Should()
            .BeEquivalentTo(
                [
                    new ViewerQueuedSongDto(
                        2,
                        "Mine A",
                        "Artist",
                        "https://img/a.png",
                        2000,
                        "K7QM"
                    ),
                    new ViewerQueuedSongDto(4, "Mine B", "Artist", null, 3000, ""),
                ],
                o => o.WithStrictOrdering()
            );
    }

    [Fact]
    public async Task Unknown_caller_is_not_found_and_never_reads_the_queue()
    {
        IMusicService music = Substitute.For<IMusicService>();
        ViewerSelfQueueService sut = new(AuthTestBuilder.NewContext(), music);

        Result<ViewerQueueDto> result = await sut.GetMyQueueAsync(Channel, Caller);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await music.DidNotReceiveWithAnyArgs().GetQueueAsync(default!, default);
    }
}
