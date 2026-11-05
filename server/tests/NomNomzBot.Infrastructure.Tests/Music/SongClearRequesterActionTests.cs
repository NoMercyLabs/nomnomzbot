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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <c>song_clear_requester</c> removes every queued song of the resolved name (the <c>!clearsongs</c>
/// capability) via <see cref="IMusicService.RemoveAllFromRequesterAsync"/>, and fails closed when the
/// requester field resolves to nothing rather than clearing an empty/arbitrary name.
/// </summary>
public sealed class SongClearRequesterActionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac201");

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = "twitch-1",
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };

    private static ActionDefinition Def(string? requester) =>
        new()
        {
            Type = "song_clear_requester",
            Parameters = requester is null
                ? null
                : new() { ["requester"] = JsonSerializer.SerializeToElement(requester) },
        };

    [Fact]
    public async Task Removes_every_queued_song_of_the_resolved_requester()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RemoveAllFromRequesterAsync(
                ChannelId.ToString(),
                "f0xb17",
                Arg.Any<CancellationToken>()
            )
            .Returns(3);
        SongClearRequesterAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def("f0xb17"));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("3").And.Contain("f0xb17");
        await music
            .Received(1)
            .RemoveAllFromRequesterAsync(
                ChannelId.ToString(),
                "f0xb17",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Fails_closed_when_the_requester_is_not_resolved_and_removes_nothing()
    {
        IMusicService music = Substitute.For<IMusicService>();
        SongClearRequesterAction action = new(music);

        ActionResult result = await action.ExecuteAsync(Ctx(), Def(null));

        result.Succeeded.Should().BeFalse();
        await music.DidNotReceiveWithAnyArgs().RemoveAllFromRequesterAsync(default!, default!);
    }
}
