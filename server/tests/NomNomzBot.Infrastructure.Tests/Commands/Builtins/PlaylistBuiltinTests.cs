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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!playlist</c> (legacy parity, S068d) proves the summary is built from the REAL queue returned by
/// <see cref="IMusicService.GetQueueAsync"/> — seeded fake current-track/queue entries must show up verbatim
/// in the rendered text, not a hardcoded string — and that a fully idle channel gets a truthful empty reply.
/// </summary>
public sealed class PlaylistBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000a601");

    private static ITemplateResolver FakeResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return resolver;
    }

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer",
        };

    [Fact]
    public async Task Reply_contains_the_real_queried_current_track_and_upcoming_entries()
    {
        NowPlaying current = new(
            TrackName: "Toxic",
            Artist: "Britney Spears",
            Album: null,
            ImageUrl: null,
            DurationMs: 200_000,
            ProgressMs: 1_000,
            IsPlaying: true,
            Volume: 60,
            RequestedBy: "viewer2",
            Provider: "spotify"
        );
        List<MusicQueueItem> queued =
        [
            new("Blinding Lights", "The Weeknd", null, 200_000, "viewer3"),
            new("Levitating", "Dua Lipa", null, 190_000, "viewer4"),
        ];

        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(current, queued));

        PlaylistBuiltin sut = new(music, new BuiltinResponseComposer(FakeResolver()));

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Toxic").And.Contain("Britney Spears");
        result.Value.Should().Contain("Blinding Lights").And.Contain("The Weeknd");
        result.Value.Should().Contain("Levitating").And.Contain("Dua Lipa");
        result.Value.Should().Contain("2");
    }

    [Fact]
    public async Task Idle_channel_with_nothing_playing_and_empty_queue_reports_truthfully()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(null, []));

        PlaylistBuiltin sut = new(music, new BuiltinResponseComposer(FakeResolver()));

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Nothing is playing").And.Contain("queue is empty");
    }
}
