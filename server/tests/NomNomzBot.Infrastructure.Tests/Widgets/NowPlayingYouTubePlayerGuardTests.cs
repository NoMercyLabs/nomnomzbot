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

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The now-playing widget plays the video the bot hands it (<c>youtube.play</c>) through the YouTube IFrame
/// Player API and reports PLAYING, PAUSED and ENDED with the position back over the overlay SDK.
///
/// <para>
/// This is a SOURCE-TEXT guard: the widget is browser Vue and this suite cannot run it; the E2E overlay
/// harness needs a live stack and a real YouTube embed. The SDK call and the hub method it reaches are proven
/// behaviourally in <c>OverlaySdkYouTubeReportTests</c> and <c>OverlayHubYouTubeReportTests</c>.
/// </para>
/// </summary>
public sealed class NowPlayingYouTubePlayerGuardTests
{
    private static string Source()
    {
        const string resourceName =
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.now_playing.vue";
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull("the now-playing widget must ship as an embedded asset");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
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
    public void The_report_carries_the_position_in_milliseconds()
    {
        Source().Should().Contain("getCurrentTime() * 1000");
    }
}
