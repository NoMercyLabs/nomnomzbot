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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A track the provider picked itself — Spotify autoplay, a playlist rolling on, YouTube's next video —
/// was NOT requested by anyone. Attributing it to "someone" invents a viewer, and it hides the one thing
/// the streamer actually needs to know from a glance: whether the request queue is feeding the stream or
/// has run dry and the provider has taken over. These hold that distinction on both branches.
/// </summary>
public sealed class CurrentSongAttributionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac901");

    private static BuiltinCommandContext Ctx() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = "Bamo",
            Args = string.Empty,
        };

    private static NowPlaying Track(string? requestedBy, string provider) =>
        new(
            TrackName: "High",
            Artist: "Basslovers United",
            Album: null,
            ImageUrl: null,
            DurationMs: 143_000,
            ProgressMs: 38_000,
            IsPlaying: true,
            Volume: 50,
            RequestedBy: requestedBy,
            Provider: provider
        );

    /// <summary>Renders the neutral fallback with the variables the builtin supplies, so the assertions are
    /// about the SENTENCE chat actually sees rather than the dictionary behind it.</summary>
    private static (CurrentSongBuiltin Builtin, IBuiltinResponseComposer Composer) Build(
        NowPlaying now
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(now);

        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest request = call.Arg<BuiltinResponseRequest>();
                string rendered = request.NeutralFallback ?? string.Empty;
                IReadOnlyDictionary<string, string> variables =
                    request.Variables ?? new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> variable in variables)
                    rendered = rendered.Replace($"{{{variable.Key}}}", variable.Value);
                return Task.FromResult(rendered);
            });

        return (new(music, composer), composer);
    }

    [Fact]
    public async Task A_requested_track_names_the_viewer_who_asked_for_it()
    {
        (CurrentSongBuiltin builtin, _) = Build(Track(requestedBy: "f0xb17", provider: "spotify"));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("High").And.Contain("Basslovers United");
        result.Value.Should().Contain("requested by f0xb17");
    }

    [Fact]
    public async Task An_autoplayed_track_says_where_it_came_from_and_never_invents_a_requester()
    {
        (CurrentSongBuiltin builtin, _) = Build(Track(requestedBy: null, provider: "spotify"));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("from spotify");
        // The exact regression: a null requester used to read as an unattributed request.
        result.Value.Should().NotContain("requested by");
        result.Value.Should().NotContain("someone");
    }

    [Fact]
    public async Task The_same_distinction_holds_for_youtube_not_just_spotify()
    {
        (CurrentSongBuiltin builtin, _) = Build(Track(requestedBy: null, provider: "youtube"));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Contain("from youtube");
        result.Value.Should().NotContain("requested by");
    }

    [Fact]
    public async Task A_blank_requester_string_counts_as_nobody_not_as_a_viewer_named_nothing()
    {
        (CurrentSongBuiltin builtin, _) = Build(Track(requestedBy: "   ", provider: "spotify"));

        Result<string> result = await builtin.ExecuteAsync(Ctx());

        result.Value.Should().Contain("from spotify");
        result.Value.Should().NotContain("requested by");
    }
}
