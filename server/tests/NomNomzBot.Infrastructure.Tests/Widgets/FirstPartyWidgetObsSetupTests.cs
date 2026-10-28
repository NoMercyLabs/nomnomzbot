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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A streamer adding an overlay to OBS must be able to read, from the page, which size the browser source needs,
/// whether to tick "Control audio via OBS" and whether to tick "Shutdown source when not visible". These facts are
/// authored once per catalogue entry; these tests prove every entry carries them and that the rules hold.
/// </summary>
public sealed class FirstPartyWidgetObsSetupTests
{
    private static readonly string[] SoundKeys =
    [
        WidgetKeyAliases.Audio,
        WidgetKeyAliases.MusicPlayer,
        "tts_caption",
    ];

    private static readonly string[] MustKeepRunningKeys =
    [
        WidgetKeyAliases.Audio,
        WidgetKeyAliases.MusicPlayer,
        "alerts",
        "tts_caption",
    ];

    [Fact]
    public void Every_catalogue_entry_carries_a_browser_source_size()
    {
        foreach (FirstPartyWidgetDefinition widget in FirstPartyWidgetCatalogue.All)
        {
            ObsBrowserSource setup = widget.ObsSetup;

            setup.Width.Should().BeGreaterThan(0, widget.Key);
            setup.Height.Should().BeGreaterThan(0, widget.Key);
        }
    }

    [Fact]
    public void Only_the_widgets_that_play_sound_ask_for_obs_audio_control()
    {
        List<string> controlled =
        [
            .. FirstPartyWidgetCatalogue
                .All.Where(w => w.ObsSetup is { ControlAudioViaObs: true })
                .Select(w => w.Key),
        ];

        controlled.Should().BeEquivalentTo(SoundKeys);
    }

    [Fact]
    public void Nothing_that_must_keep_playing_or_listening_is_shut_down_when_hidden()
    {
        foreach (string key in MustKeepRunningKeys)
            FirstPartyWidgetCatalogue.Find(key)!.ObsSetup.ShutdownWhenHidden.Should().BeFalse(key);
    }

    [Fact]
    public void The_tts_caption_is_a_full_canvas_source_that_ticks_obs_audio_control_because_it_plays_the_speech()
    {
        FirstPartyWidgetDefinition caption = FirstPartyWidgetCatalogue.Find("tts_caption")!;

        caption.ObsSetup.Should().Be(new ObsBrowserSource(1920, 1080, true, false));
        caption.Description.Should().Contain("keep \"Control audio via OBS\" ON");
    }

    [Fact]
    public void The_two_audio_sources_are_one_pixel_and_tick_obs_audio_control()
    {
        FirstPartyWidgetCatalogue
            .Find(WidgetKeyAliases.Audio)!
            .ObsSetup.Should()
            .Be(new ObsBrowserSource(1, 1, true, false));
        FirstPartyWidgetCatalogue
            .Find(WidgetKeyAliases.MusicPlayer)!
            .ObsSetup.Should()
            .Be(new ObsBrowserSource(1, 1, true, false));
    }
}
