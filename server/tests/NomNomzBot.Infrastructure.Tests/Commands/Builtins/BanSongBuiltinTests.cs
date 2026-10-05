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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!bansong</c> (legacy parity, S068c) proves the REAL side effect: the currently playing track is
/// actually handed to <see cref="IBlockedTrackService"/> with the right provider/URI/title, not merely
/// "no exception" — and that nothing is banned when nothing is playing. Also proves the "nothing playing"
/// copy is tone-styled (S069h).
/// </summary>
public sealed class BanSongBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009902");

    private static BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "mod-1",
            TriggeringUserDisplayName = "SomeMod",
            RoleLevel = 10,
            Personality = personality,
        };

    private static IBuiltinResponseComposer FakeComposer(
        IChannelBuiltinReplyOverrides? channelReplies = null
    )
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
            channelReplies ?? FakeChannelBuiltinReplies.None
        );
    }

    private const string Confirmation = "Toxic banned from being requested again.";

    private const string StrikeWarning =
        "You have gotten 5 banned songs now. Don't get your ass banned from this feature.";

    private const string StrikeRevoked =
        "Your permission to redeem songs have been revoked, points will not be refunded if you request more";

    // The old bot's own thresholds: the warning prints for 6 to 10 bans, the revoke notice from 11 on.
    [Theory]
    [InlineData(1, Confirmation)]
    [InlineData(5, Confirmation)]
    [InlineData(6, Confirmation + " " + StrikeWarning)]
    [InlineData(10, Confirmation + " " + StrikeWarning)]
    [InlineData(11, Confirmation + " " + StrikeRevoked)]
    [InlineData(40, Confirmation + " " + StrikeRevoked)]
    public async Task The_strike_notice_follows_the_old_bots_thresholds_for_the_moderators_own_bans(
        int strikeCount,
        string expected
    )
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Toxic",
                    "Britney Spears",
                    null,
                    null,
                    200_000,
                    1_000,
                    true,
                    50,
                    null,
                    "spotify",
                    "spotify:track:toxic"
                )
            );
        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:toxic",
                        "Toxic",
                        null,
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );
        blockedTracks
            .CountByBlockerAsync(Broadcaster, "mod-1", Arg.Any<CancellationToken>())
            .Returns(strikeCount);
        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> result = await sut.ExecuteAsync(Context());

        result.Value.Should().Be(expected);
    }

    [Fact]
    public async Task Banning_the_playing_track_calls_BlockAsync_with_its_real_provider_uri_and_title()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Never Gonna Give You Up",
                    Artist: "Rick Astley",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 213_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: "viewer1",
                    Provider: "spotify",
                    TrackUri: "spotify:track:rick1"
                )
            );

        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:rick1",
                        "Never Gonna Give You Up",
                        "Banned via !bansong",
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );

        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Never Gonna Give You Up banned from being requested again.");

        await blockedTracks
            .Received(1)
            .BlockAsync(
                Broadcaster,
                Arg.Is<BlockTrackRequest>(r =>
                    r.Provider == "spotify"
                    && r.TrackUri == "spotify:track:rick1"
                    && r.Title == "Never Gonna Give You Up"
                    && r.BlockedByUserId == "mod-1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_channel_can_reword_the_ban_confirmation_and_the_title_still_fills_in()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Toxic",
                    Artist: "Britney Spears",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 200_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: null,
                    Provider: "spotify",
                    TrackUri: "spotify:track:toxic"
                )
            );
        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:toxic",
                        "Toxic",
                        "Banned via !bansong",
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );
        FakeChannelBuiltinReplies channel = new FakeChannelBuiltinReplies().Set(
            Broadcaster,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Banned,
            "{track.name} is gone for good."
        );
        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(channel),
            MusicGateTestKit.Gate(false)
        );

        Result<string> result = await sut.ExecuteAsync(Context());

        result.Value.Should().Be("Toxic is gone for good.");
    }

    [Fact]
    public async Task Banning_skips_the_track_and_stores_the_reason_the_mod_typed()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Bad Song",
                    "Artist",
                    null,
                    null,
                    200_000,
                    1_000,
                    true,
                    50,
                    null,
                    "spotify",
                    "spotify:track:bad"
                )
            );
        music
            .SkipAsync(
                Broadcaster.ToString(),
                "mod-1",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:bad",
                        "Bad Song",
                        "not stream-safe",
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );
        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        BuiltinCommandContext context = new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "mod-1",
            TriggeringUserDisplayName = "SomeMod",
            RoleLevel = 10,
            Personality = PersonalityTone.Informative,
            Args = "not stream-safe",
        };
        Result<string> result = await sut.ExecuteAsync(context);

        result.Value.Should().Be("Bad Song banned from being requested again.");
        await blockedTracks
            .Received(1)
            .BlockAsync(
                Broadcaster,
                Arg.Is<BlockTrackRequest>(r => r.Reason == "not stream-safe"),
                Arg.Any<CancellationToken>()
            );
        await music
            .Received(1)
            .SkipAsync(
                Broadcaster.ToString(),
                "mod-1",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Banning_with_no_reason_stores_no_reason_and_a_failed_skip_still_confirms_the_ban()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "S",
                    "A",
                    null,
                    null,
                    1,
                    1,
                    true,
                    50,
                    null,
                    "spotify",
                    "spotify:track:s"
                )
            );
        music
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("nope", "PROVIDER_UNAVAILABLE"));
        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new BlockedTrackDto(
                        Guid.CreateVersion7(),
                        "spotify",
                        "spotify:track:s",
                        "S",
                        null,
                        "mod-1",
                        DateTime.UtcNow
                    )
                )
            );
        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> result = await sut.ExecuteAsync(Context());

        result.Value.Should().Be("S banned from being requested again.");
        await blockedTracks
            .Received(1)
            .BlockAsync(
                Broadcaster,
                Arg.Is<BlockTrackRequest>(r => r.Reason == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Nothing_playing_never_calls_BlockAsync()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();

        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("No song is currently playing!");

        await blockedTracks
            .DidNotReceive()
            .BlockAsync(
                Arg.Any<Guid>(),
                Arg.Any<BlockTrackRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_nothing_playing_message_than_the_default_tone()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);
        BanSongBuiltin sut = new(
            music,
            Substitute.For<IBlockedTrackService>(),
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> sassy = await sut.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await sut.ExecuteAsync(Context());

        informative.Value.Should().Be("No song is currently playing!");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.BanSong.Key,
                BuiltinResponseSlots.BanSong.Nothing
            )
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_could_not_ban_message_than_the_default_tone()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Track",
                    Artist: "Artist",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 180_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: null,
                    Provider: "spotify",
                    TrackUri: "spotify:track:x"
                )
            );

        IBlockedTrackService blockedTracks = Substitute.For<IBlockedTrackService>();
        blockedTracks
            .BlockAsync(Broadcaster, Arg.Any<BlockTrackRequest>(), Arg.Any<CancellationToken>())
            // No service-supplied ErrorMessage -> forces the tone-styled generic fallback.
            .Returns(Result.Failure<BlockedTrackDto>(null!, "UNKNOWN"));

        BanSongBuiltin sut = new(
            music,
            blockedTracks,
            FakeComposer(),
            MusicGateTestKit.Gate(false)
        );

        Result<string> sassy = await sut.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await sut.ExecuteAsync(Context());

        informative.Value.Should().Be("Could not ban that track — try again in a moment.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.BanSong.Key,
                BuiltinResponseSlots.BanSong.CouldNotBan
            )
            .Should()
            .Contain(sassy.Value);
    }
}
