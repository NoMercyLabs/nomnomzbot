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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Holds <c>!skip</c>, <c>!volume</c> and <c>!song</c> to the old bot's behavior and wording (legacy
/// parity ledger rows S-PAR-CMD-SKIP, S-PAR-CMD-VOLUME, S-PAR-CMD-SONG). The informative tone is the
/// legacy sentence verbatim; every assertion on state also asserts what the provider was (not) told.
/// </summary>
public sealed class MusicCommandParityTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac003");

    private static BuiltinCommandContext Ctx(
        string args,
        int roleLevel = 0,
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = "Bamo",
            TriggeringUserLogin = "bamo",
            RoleLevel = roleLevel,
            Args = args,
            Personality = personality,
        };

    private static IBuiltinResponseComposer FakeComposer()
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
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

    private static NowPlaying Playing(
        string? requestedBy,
        string? uri = "spotify:track:4PTG3Z6ehGkBFwjybzWkR8",
        int volume = 40
    ) =>
        new(
            TrackName: "High",
            Artist: "Basslovers United",
            Album: null,
            ImageUrl: null,
            DurationMs: 143_000,
            ProgressMs: 38_000,
            IsPlaying: true,
            Volume: volume,
            RequestedBy: requestedBy,
            Provider: "spotify",
            TrackUri: uri
        );

    private static IMusicService MusicWith(NowPlaying? now)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>()).Returns(now);
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        music
            .SetVolumeAsync(Broadcaster.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return music;
    }

    private static SkipBuiltin Skip(IMusicService music, bool holdsGrant = false) =>
        new(music, FakeComposer(), MusicGateTestKit.Gate(holdsGrant));

    private static VolumeBuiltin Volume(IMusicService music) =>
        new(music, FakeComposer(), MusicGateTestKit.Gate(true));

    // ── !skip ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_viewer_skips_the_song_they_requested_and_the_provider_advances()
    {
        IMusicService music = MusicWith(Playing(requestedBy: "Bamo"));

        Result<string> result = await Skip(music).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("Skipped your song.");
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_viewer_cannot_skip_a_song_someone_else_requested()
    {
        IMusicService music = MusicWith(Playing(requestedBy: "SomeoneElse"));

        Result<string> result = await Skip(music).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("You can only skip songs you requested yourself.");
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }

    [Fact]
    public async Task A_viewer_cannot_skip_a_track_nobody_requested()
    {
        IMusicService music = MusicWith(Playing(requestedBy: null));

        Result<string> result = await Skip(music).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("You can only skip songs you requested yourself.");
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }

    [Fact]
    public async Task A_viewer_who_skips_with_nothing_playing_hears_that_nothing_is_playing()
    {
        IMusicService music = MusicWith(null);

        Result<string> result = await Skip(music).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("No song is currently playing!");
        await music.DidNotReceiveWithAnyArgs().SkipAsync(default!, default!);
    }

    [Fact]
    public async Task A_moderator_skips_any_song_and_hears_the_neutral_legacy_sentence()
    {
        IMusicService music = MusicWith(Playing(requestedBy: "SomeoneElse"));

        Result<string> result = await Skip(music).ExecuteAsync(Ctx(string.Empty, roleLevel: 10));

        result.Value.Should().Be("Skipped to the next track.");
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_grant_holder_skips_any_song_like_a_moderator()
    {
        IMusicService music = MusicWith(Playing(requestedBy: "SomeoneElse"));

        Result<string> result = await Skip(music, holdsGrant: true).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("Skipped to the next track.");
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "twitch-42",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void Every_tone_of_the_viewer_skip_replies_carries_a_line()
    {
        foreach (string slot in new[] { "skippedown", "notyours", "nothingplaying" })
        {
            IReadOnlyList<string> informative = ToneTemplateCatalog.Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.Skip.Key,
                slot
            );
            informative.Should().NotBeEmpty($"slot {slot} needs an informative line");
            foreach (
                string tone in PersonalityTone.All.Where(t => t != PersonalityTone.Informative)
            )
                ToneTemplateCatalog
                    .Get(tone, BuiltinResponseSlots.Skip.Key, slot)
                    .Should()
                    .NotBeEquivalentTo(informative, $"tone {tone} needs its own {slot} voice");
        }
    }

    // ── !volume ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Volume_with_no_argument_reports_the_level_in_the_legacy_words()
    {
        IMusicService music = MusicWith(Playing(requestedBy: null, volume: 40));

        Result<string> result = await Volume(music).ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("Current volume level is 40");
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("55")]
    public async Task Volume_with_nothing_playing_answers_like_the_legacy_bot_and_sets_nothing(
        string args
    )
    {
        IMusicService music = MusicWith(null);

        Result<string> result = await Volume(music).ExecuteAsync(Ctx(args));

        result.Value.Should().Be("No song is currently playing!");
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default);
    }

    [Theory]
    [InlineData("150")]
    [InlineData("-5")]
    [InlineData("101")]
    [InlineData("loud")]
    public async Task Volume_rejects_an_out_of_range_or_non_numeric_level_instead_of_clamping(
        string args
    )
    {
        IMusicService music = MusicWith(Playing(requestedBy: null));

        Result<string> result = await Volume(music).ExecuteAsync(Ctx(args));

        result
            .Value.Should()
            .Be("Please provide a valid volume level between 0 and 100: !volume <level> (0-100).");
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("55")]
    public async Task Volume_sets_an_in_range_level_and_confirms_it(string args)
    {
        IMusicService music = MusicWith(Playing(requestedBy: null));

        Result<string> result = await Volume(music).ExecuteAsync(Ctx(args));

        result.Value.Should().Be($"Volume set to {args}%.");
        await music
            .Received(1)
            .SetVolumeAsync(Broadcaster.ToString(), int.Parse(args), Arg.Any<CancellationToken>());
    }

    // ── !song ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Song_names_the_track_artist_and_a_clickable_link_like_the_legacy_bot()
    {
        IMusicService music = MusicWith(Playing(requestedBy: "f0xb17"));
        CurrentSongBuiltin sut = new(music, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        result
            .Value.Should()
            .Be(
                "The current song is: High by Basslovers United https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8"
            );
    }

    [Fact]
    public async Task Song_for_a_provider_that_hands_back_a_web_url_passes_it_through()
    {
        IMusicService music = MusicWith(
            Playing(requestedBy: null, uri: "https://www.youtube.com/watch?v=dQw4w9WgXcQ")
        );
        CurrentSongBuiltin sut = new(music, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        result
            .Value.Should()
            .Be(
                "The current song is: High by Basslovers United https://www.youtube.com/watch?v=dQw4w9WgXcQ"
            );
    }

    [Fact]
    public async Task Song_without_a_known_uri_has_no_link_and_no_trailing_space()
    {
        IMusicService music = MusicWith(Playing(requestedBy: null, uri: null));
        CurrentSongBuiltin sut = new(music, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("The current song is: High by Basslovers United");
    }

    [Fact]
    public async Task Song_with_nothing_playing_answers_in_the_legacy_words()
    {
        IMusicService music = MusicWith(null);
        CurrentSongBuiltin sut = new(music, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Ctx(string.Empty));

        result.Value.Should().Be("No song is currently playing!");
    }

    [Fact]
    public async Task Song_in_the_sassy_tone_keeps_its_voice_and_the_same_facts()
    {
        IMusicService music = MusicWith(Playing(requestedBy: null));
        CurrentSongBuiltin sut = new(music, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(
            Ctx(string.Empty, personality: PersonalityTone.Sassy)
        );

        result.Value.Should().Contain("High").And.Contain("Basslovers United");
        result.Value.Should().Contain("https://open.spotify.com/track/4PTG3Z6ehGkBFwjybzWkR8");
        result.Value.Should().NotBe("The current song is: High by Basslovers United");
    }
}
