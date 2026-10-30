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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A request that waits to start (the director is Starting) never freezes the owner's level controls:
/// volume, mute and duck reach the item's engine exactly as they do while Playing. Pause, skip, previous
/// and seek have nothing to act on yet and stay ignored (journaled "ignored", no engine call).
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    /// <summary>A request is admitted and the engine accepted its start, so the director waits in Starting.</summary>
    private static async Task<(Rig Rig, PlaybackItem Item)> StartingOnAsync(EngineKind kind)
    {
        Rig rig = new();
        PlaybackItem item = Item("w1", kind);
        await rig.Request(item);
        rig.Director.State.Should().Be(PlaybackState.Starting(kind, item));

        return (rig, item);
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerVolume_while_Starting_reaches_the_items_engine_and_journals_volume(
        EngineKind kind
    )
    {
        (Rig rig, PlaybackItem item) = await StartingOnAsync(kind);

        await rig.Director.HandleAsync(new OwnerVolume(40));

        rig.Director.State.Should().Be(PlaybackState.Starting(kind, item));
        rig.Director.Level.Should().Be(new PlaybackLevel(40, false));
        rig.Engine(kind)
            .Calls.Should()
            .Equal(new EngineCall("Start", "w1"), new EngineCall("SetVolume", "40"));
        rig.Other(kind).Calls.Should().BeEmpty();
        rig.Journal.Last().Input.Should().Be("OwnerVolume");
        rig.Journal.Last().Reason.Should().Be("volume");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task OwnerMute_while_Starting_silences_then_restores_the_level(EngineKind kind)
    {
        (Rig rig, PlaybackItem item) = await StartingOnAsync(kind);
        await rig.Director.HandleAsync(new OwnerVolume(70));

        await rig.Director.HandleAsync(new OwnerMute(true));
        await rig.Director.HandleAsync(new OwnerMute(false));

        rig.Director.State.Should().Be(PlaybackState.Starting(kind, item));
        rig.Director.Level.Should().Be(new PlaybackLevel(70, false));
        rig.Engine(kind)
            .Calls.Skip(1)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "70"),
                new EngineCall("SetVolume", "0"),
                new EngineCall("SetVolume", "70")
            );
        rig.Journal.TakeLast(3).Select(e => e.Reason).Should().Equal("volume", "mute", "unmute");
    }

    [Theory]
    [InlineData(EngineKind.Spotify)]
    [InlineData(EngineKind.YouTube)]
    public async Task DuckStart_and_DuckEnd_while_Starting_drop_and_restore_the_music_level(
        EngineKind kind
    )
    {
        (Rig rig, PlaybackItem item) = await StartingOnAsync(kind);
        await rig.Director.HandleAsync(new OwnerVolume(80));

        await rig.Director.HandleAsync(new DuckStart(50));
        await rig.Director.HandleAsync(new DuckEnd());

        rig.Director.State.Should().Be(PlaybackState.Starting(kind, item));
        rig.Engine(kind)
            .Calls.Skip(1)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "80"),
                new EngineCall("SetVolume", "40"),
                new EngineCall("SetVolume", "80")
            );
        rig.Journal.TakeLast(2).Select(e => e.Reason).Should().Equal("duck_start", "duck_end");
    }

    [Fact]
    public async Task HandleAsync_reports_Applied_for_a_level_in_Starting_and_Ignored_for_a_pause()
    {
        (Rig rig, _) = await StartingOnAsync(EngineKind.Spotify);

        PlaybackInputOutcome level = await rig.Director.HandleAsync(new OwnerVolume(40));
        PlaybackInputOutcome pause = await rig.Director.HandleAsync(new OwnerPause());
        PlaybackInputOutcome volumeAgain = await rig.Director.HandleAsync(new OwnerVolume(41));

        level.Should().Be(PlaybackInputOutcome.Applied);
        pause.Should().Be(PlaybackInputOutcome.Ignored);
        volumeAgain
            .Should()
            .Be(
                PlaybackInputOutcome.Applied,
                "an earlier ignored input does not leak into the next one"
            );
    }

    [Fact]
    public async Task A_level_set_while_Starting_is_kept_when_the_song_then_plays()
    {
        (Rig rig, PlaybackItem item) = await StartingOnAsync(EngineKind.Spotify);
        await rig.Director.HandleAsync(new OwnerVolume(25));

        await rig.Plays(rig.Spotify, item);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, item));
        rig.Director.Level.Should().Be(new PlaybackLevel(25, false));
    }

    [Theory]
    [InlineData("OwnerPause")]
    [InlineData("OwnerSkip")]
    [InlineData("OwnerPrevious")]
    [InlineData("OwnerSeek")]
    [InlineData("OwnerResume")]
    public async Task Pause_skip_previous_seek_and_resume_while_Starting_stay_ignored_with_no_engine_call(
        string control
    )
    {
        (Rig rig, PlaybackItem item) = await StartingOnAsync(EngineKind.Spotify);
        PlaybackInput input = control switch
        {
            "OwnerPause" => new OwnerPause(),
            "OwnerSkip" => new OwnerSkip(),
            "OwnerPrevious" => new OwnerPrevious(),
            "OwnerSeek" => new OwnerSeek(30_000),
            _ => new OwnerResume(),
        };

        await rig.Director.HandleAsync(input);

        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, item));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "w1"));
        rig.Journal.Last().Input.Should().Be(control);
        rig.Journal.Last().Reason.Should().Be("ignored");
    }
}
