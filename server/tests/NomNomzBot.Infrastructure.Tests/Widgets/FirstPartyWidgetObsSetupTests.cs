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
        "media_share",
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
    public void The_tts_caption_is_a_caption_sized_source_that_ticks_obs_audio_control_because_it_plays_the_speech()
    {
        FirstPartyWidgetDefinition caption = FirstPartyWidgetCatalogue.Find("tts_caption")!;

        caption.ObsSetup.Should().Be(new ObsBrowserSource(1040, 200, true, false));
        caption.Description.Should().Contain("keep \"Control audio via OBS\" ON");
    }

    [Fact]
    public void The_media_share_player_is_a_video_sized_source_that_ticks_obs_audio_control_because_the_video_has_sound()
    {
        FirstPartyWidgetCatalogue
            .Find("media_share")!
            .ObsSetup.Should()
            .Be(new ObsBrowserSource(854, 520, true, false));
    }

    [Fact]
    public void No_widget_that_draws_is_a_full_canvas_source_unless_it_is_on_the_named_full_screen_list()
    {
        string[] fullScreenKeys = ["drop_game", "emote_wall", "raid_recap", "stage"];
        string[] soundOnlyKeys = [WidgetKeyAliases.Audio, WidgetKeyAliases.MusicPlayer];

        List<FirstPartyWidgetDefinition> drawing =
        [
            .. FirstPartyWidgetCatalogue.All.Where(w => !soundOnlyKeys.Contains(w.Key)),
        ];

        drawing.Should().HaveCount(25);
        foreach (FirstPartyWidgetDefinition widget in drawing)
        {
            ObsBrowserSource setup = widget.ObsSetup;
            bool isFullCanvas = setup.Width == 1920 && setup.Height == 1080;

            setup.Width.Should().BeGreaterThan(1, widget.Key);
            setup.Height.Should().BeGreaterThan(1, widget.Key);
            isFullCanvas
                .Should()
                .Be(
                    fullScreenKeys.Contains(widget.Key),
                    $"{widget.Key} is 1920 x 1080 only if it is full screen"
                );
        }
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
