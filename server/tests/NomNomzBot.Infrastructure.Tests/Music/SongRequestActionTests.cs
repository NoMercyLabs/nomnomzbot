// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the <c>song_request</c> pipeline action (the reward/pipeline-triggered twin of <c>!sr</c>) resolves
/// via <see cref="IMusicService.RequestTrackAsync"/> — a track link or a search query, single resolve — and
/// answers each refusal reason with its own chat wording instead of one blanket failure: not-found, a
/// blocked track (carries its typed reason), no provider connected, and a genuinely erroring provider are
/// four different messages, not one.
/// </summary>
public sealed class SongRequestActionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac101");

    [Fact]
    public async Task A_resolved_track_is_queued_and_a_confirmation_is_sent()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Success(
                new MusicTrack("spotify:track:abc", "Song Q", "Artist", null, null, 0, "spotify")
            )
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        result.Succeeded.Should().BeTrue();
        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("Song Q") && m.Contains("Artist")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Not_found_sends_a_not_found_message_and_fails_the_step()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("No tracks found for \"xyz\".", "NOT_FOUND")
        );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Def("xyz"));

        result.Succeeded.Should().BeFalse();
        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("No tracks found")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_blocked_track_carries_its_typed_reason_into_chat()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("\"Song Q\" is blocked in this channel.", "TRACK_BLOCKED")
        );

        await sut.ExecuteAsync(Ctx(), Def("song q"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("is blocked in this channel")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task No_provider_connected_says_requests_are_not_set_up_not_could_not_add()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("No active music provider.", "SERVICE_UNAVAILABLE")
        );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m => m.Contains("aren't set up")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_genuinely_erroring_provider_reads_differently_from_no_provider_and_not_found()
    {
        (SongRequestAction sut, IMusicService music, IChatProvider chat) = Build(
            Result.Failure<MusicTrack>("token refresh failed", "PROVIDER_ERROR")
        );

        await sut.ExecuteAsync(Ctx(), Def("lofi beats"));

        await chat.Received()
            .SendMessageAsync(
                ChannelId,
                Arg.Is<string>(m =>
                    m.Contains("try again in a moment")
                    && !m.Contains("aren't set up")
                    && !m.Contains("No tracks found")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (SongRequestAction Sut, IMusicService Music, IChatProvider Chat) Build(
        Result<MusicTrack> requestResult
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                ChannelId.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(requestResult);

        IChatProvider chat = Substitute.For<IChatProvider>();
        SongRequestAction sut = new(music, chat, NullLogger<SongRequestAction>.Instance);
        return (sut, music, chat);
    }

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-42",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!sr",
        };

    private static ActionDefinition Def(string query) =>
        new()
        {
            Type = "song_request",
            Parameters = new() { ["query"] = JsonSerializer.SerializeToElement(query) },
        };
}
