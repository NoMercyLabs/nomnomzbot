// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The Audio widget plays the video the bot hands it (<c>youtube.play</c>) through the YouTube IFrame
/// Player API and reports PLAYING, PAUSED and ENDED with the position back over the overlay SDK.
///
/// <para>
/// This is a SOURCE-TEXT guard: the widget is browser Vue and this suite cannot run it; the E2E overlay
/// harness needs a live stack and a real YouTube embed. The SDK call and the hub method it reaches are proven
/// behaviourally in <c>OverlaySdkYouTubeReportTests</c> and <c>OverlayHubYouTubeReportTests</c>.
/// </para>
/// </summary>
public sealed class MusicPlayerYouTubeGuardTests
{
    private static string Source() => Asset("music_player");

    private static string Asset(string key)
    {
        string resourceName = $"NomNomzBot.Infrastructure.Content.Widgets.Assets.{key}.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the {key} widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_display_widget_does_not_create_a_second_youtube_player()
    {
        const string displayResource =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.now_playing.vue";
        using System.IO.Stream? stream =
            typeof(FirstPartyWidgetCatalogue).Assembly.GetManifestResourceStream(displayResource);
        stream.Should().NotBeNull();
        using StreamReader reader = new(stream);
        string display = reader.ReadToEnd();

        display.Should().NotContain("new YT.Player(");
        display.Should().NotContain("NomNomz.on('youtube.play'");
        display.Should().NotContain("new Spotify.Player(");
        display.Should().Contain("NomNomz.on('now_playing'");
    }

    [Theory]
    [InlineData("new YT.Player(")]
    [InlineData("new Spotify.Player(")]
    [InlineData("enableAudio")]
    [InlineData("youtube.play")]
    [InlineData("now_playing")]
    public void The_bot_audio_widget_no_longer_hosts_any_music_player(string fragment)
    {
        Asset("audio").Should().NotContain(fragment);
    }

    [Fact]
    public void The_bot_audio_widget_still_shows_the_tts_indicator()
    {
        Asset("audio").Should().Contain("NomNomz.on('tts_speak'");
    }

    [Fact]
    public void The_music_player_applies_its_own_level_and_mute_to_both_players()
    {
        string source = Source();

        source.Should().Contain("cfg.volume");
        source.Should().Contain("cfg.muted");
        source.Should().Contain("ytPlayer?.setVolume(ytLevel())");
        source.Should().Contain("cfg.volume / 100");
        source.Should().NotContain("volume: 1.0");
    }

    [Fact]
    public void The_music_player_does_not_speak_tts()
    {
        Source().Should().NotContain("tts_speak");
    }

    [Fact]
    public void The_audio_widget_hosts_the_spotify_connect_player()
    {
        string source = Source();
        source.Should().Contain("new Spotify.Player(");
        source.Should().Contain("NomNomz.spotify.playbackToken()");
        source.Should().Contain("NomNomz.on('now_playing', onAudioNowPlaying)");
    }

    [Fact]
    public void The_widget_listens_for_the_youtube_play_event_the_bot_sends()
    {
        string source = Source();

        source.Should().Contain("NomNomz.on('youtube.play'");
        source.Should().Contain("NomNomz.off('youtube.play'");
    }

    [Theory]
    [InlineData("youtube.pause", "pauseVideo()")]
    [InlineData("youtube.resume", "playVideo()")]
    [InlineData("youtube.stop", "stopVideo()")]
    [InlineData("youtube.seek", "seekTo(")]
    [InlineData("youtube.volume", "setVolume(ytLevel())")]
    public void The_widget_pauses_and_resumes_the_player_on_the_bots_transport_events(
        string eventType,
        string playerCall
    )
    {
        string source = Source();

        source.Should().Contain($"NomNomz.on('{eventType}'");
        source.Should().Contain($"NomNomz.off('{eventType}'");
        source.Should().Contain(playerCall);
    }

    [Fact]
    public void The_video_plays_through_the_iframe_player_api_so_state_changes_are_visible()
    {
        string source = Source();

        source.Should().Contain("https://www.youtube.com/iframe_api");
        source.Should().Contain("new YT.Player(");
        source.Should().Contain("onStateChange");
        source.Should().NotContain("youtube-nocookie.com/embed/${youtubeVideoId}");
    }

    [Theory]
    [InlineData("PLAYING")]
    [InlineData("PAUSED")]
    [InlineData("ENDED")]
    public void Each_player_state_is_reported_back_to_the_bot(string state)
    {
        string source = Source();

        source.Should().Contain("NomNomz.reportYouTubePlayerState(");
        source.Should().Contain($"'{state}'");
    }

    [Fact]
    public void A_playing_or_paused_video_is_re_reported_well_inside_the_bots_staleness_window()
    {
        // The bot treats a report older than Staleness as a closed page and an idle player; a song longer
        // than that is then cut off by the next request. The widget must heartbeat faster than that.
        string source = Source();
        System.Text.RegularExpressions.Match heartbeat = System.Text.RegularExpressions.Regex.Match(
            source,
            @"const YT_HEARTBEAT_MS: number = (\d+)"
        );
        heartbeat.Success.Should().BeTrue("the widget must declare its heartbeat interval");

        TimeSpan interval = TimeSpan.FromMilliseconds(int.Parse(heartbeat.Groups[1].Value));
        interval.Should().BeLessThan(YouTubePlayerStateStore.Staleness / 2);
        source.Should().Contain("window.setInterval(reportYouTubeHeartbeat, YT_HEARTBEAT_MS)");
        source.Should().Contain("window.clearInterval(ytHeartbeat)");
    }

    [Fact]
    public void A_video_the_player_cannot_play_is_reported_as_an_error_so_the_queue_moves_on()
    {
        // Embedding disabled, removed, private or region-blocked videos fail through onError, never through
        // onStateChange; without this the bot waits on a song that will never start.
        string source = Source();

        source.Should().Contain("onError: (e) => reportYouTubeError(");
        source.Should().Contain("NomNomz.reportYouTubePlayerState(videoId, 'ERROR'");
    }

    [Fact]
    public void Commands_that_arrive_before_the_player_is_ready_or_while_it_loads_are_held_not_lost()
    {
        // The IFrame player has no methods until onReady; a pause, stop or second play before then threw. A
        // resumed paused song arrives as play + seek + pause, and a pause during loading is ignored by YouTube,
        // so the song played instead of staying paused.
        string source = Source();

        source.Should().Contain("onReady: (e) => onYouTubeReady(e.target, startedWith)");
        source.Should().NotContain("ytPlayer = new YT.Player(");
        source.Should().Contain("if (ytLoading || !ytPlayer) { ytPendingPause = true; return }");
        source.Should().Contain("if (ytPendingPause) player.pauseVideo()");
    }

    [Fact]
    public void A_page_rejoining_the_bot_reports_its_video_before_the_bot_hands_anything_over()
    {
        // After a bot restart the page keeps playing but the bot's player memory is empty. The SDK re-applies
        // settings on every join, just before it tells the bot the page is ready: reporting there marks the
        // player busy, so the next request waits instead of being pushed over the song.
        string source = Source();

        source.Should().Contain("NomNomz.onSettings(onSettingsOrRejoin)");
        source.Should().Contain("function onSettingsOrRejoin(");
        source.Should().Contain("reportYouTubeHeartbeat()");
    }

    [Fact]
    public void A_video_that_starts_muted_is_unmuted_or_asks_for_a_click()
    {
        // Without a user gesture YouTube autoplays muted and still reports PLAYING (Fillz, 2026-10-18: "playing"
        // but no audio). On PLAYING the page unmutes, and shows the enable-audio button when it may not play sound.
        string source = Source();

        source.Should().Contain("ensureYouTubeAudible(player)");
        source.Should().Contain("player.unMute()");
        source.Should().Contain("if (!allowed || player.isMuted()) spotifyStatus.value = 'muted'");
        source.Should().Contain("ytPlayer?.unMute()");
    }

    [Fact]
    public void The_report_carries_the_position_in_milliseconds()
    {
        Source().Should().Contain("getCurrentTime() * 1000");
    }
}
