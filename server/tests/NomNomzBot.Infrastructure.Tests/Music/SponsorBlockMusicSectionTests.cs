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

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NomNomzBot.Infrastructure.Music.SponsorBlock;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// SponsorBlock music section: a YouTube request plays from the end of an off-topic intro to the start of an
/// off-topic outro, the engine and the now-playing read report the trimmed length, and a service that fails
/// leaves the video playing whole.
/// </summary>
public sealed class SponsorBlockMusicSectionTests
{
    private const string VideoId = "zzTestVid01";
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000c4b01");
    private static readonly Guid PlayerWidget = Guid.Parse("0192b000-0000-7000-8000-0000000c4b02");

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw new HttpRequestException("service down");
    }

    private sealed class RecordingNotifier : IWidgetEventNotifier
    {
        public List<(string EventType, object? Data)> Sent { get; } = [];

        public Task SendWidgetEventAsync(
            Guid broadcasterId,
            Guid widgetId,
            string eventType,
            object? data,
            CancellationToken ct = default
        )
        {
            Sent.Add((eventType, data));
            return Task.CompletedTask;
        }
    }

    private static string Marks(params (double Start, double End)[] spans)
    {
        string segments = string.Join(
            ",",
            spans.Select(s =>
                FormattableString.Invariant(
                    $"{{\"category\":\"music_offtopic\",\"actionType\":\"skip\",\"segment\":[{s.Start},{s.End}],\"videoDuration\":200}}"
                )
            )
        );

        return $"[{{\"videoID\":\"{VideoId}\",\"hash\":\"abc\",\"segments\":[{segments}]}}]";
    }

    private static RecordingHttpHandler Service(HttpStatusCode status, string? json = null)
    {
        RecordingHttpHandler handler = new();
        handler.RespondWhen(r => r.RequestUri!.Host == "sponsor.ajay.app", status, json);

        return handler;
    }

    private static SponsorBlockClient ClientOver(
        HttpMessageHandler handler,
        FakeTimeProvider clock
    ) => new(new SingleHandlerClientFactory(handler), clock);

    private static FakeTimeProvider Clock() => new(DateTimeOffset.Parse("2026-11-17T12:00:00Z"));

    [Fact]
    public async Task An_intro_mark_moves_the_start_and_an_outro_mark_moves_the_end()
    {
        RecordingHttpHandler handler = Service(HttpStatusCode.OK, Marks((0, 12.5), (180, 200)));
        SponsorBlockClient client = ClientOver(handler, Clock());

        YouTubeMusicSection? section = await client.ReadMusicSectionAsync(VideoId, 200_000);

        section.Should().Be(new YouTubeMusicSection(12_500, 180_000));
    }

    [Fact]
    public async Task A_middle_mark_is_ignored_and_the_whole_video_plays()
    {
        RecordingHttpHandler handler = Service(HttpStatusCode.OK, Marks((90, 110)));
        SponsorBlockClient client = ClientOver(handler, Clock());

        YouTubeMusicSection? section = await client.ReadMusicSectionAsync(VideoId, 200_000);

        section.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_length_takes_the_outro_from_the_length_in_the_mark()
    {
        RecordingHttpHandler handler = Service(HttpStatusCode.OK, Marks((185, 200)));
        SponsorBlockClient client = ClientOver(handler, Clock());

        YouTubeMusicSection? section = await client.ReadMusicSectionAsync(VideoId, 0);

        section.Should().Be(new YouTubeMusicSection(0, 185_000));
    }

    [Fact]
    public async Task The_lookup_sends_a_hash_prefix_and_never_the_video_id()
    {
        RecordingHttpHandler handler = Service(HttpStatusCode.OK, Marks((0, 10)));
        SponsorBlockClient client = ClientOver(handler, Clock());

        await client.ReadMusicSectionAsync(VideoId, 200_000);

        handler.RequestUrls.Should().ContainSingle();
        handler.RequestUrls[0].Should().NotContain(VideoId).And.Contain("category=music_offtopic");
        handler.RequestUrls[0].Should().MatchRegex(@"skipSegments/[0-9a-f]{4}\?");
    }

    [Fact]
    public async Task Two_reads_of_one_video_fetch_once_and_a_404_is_cached_as_no_marks()
    {
        RecordingHttpHandler handler = Service(HttpStatusCode.NotFound);
        FakeTimeProvider clock = Clock();
        SponsorBlockClient client = ClientOver(handler, clock);

        YouTubeMusicSection? first = await client.ReadMusicSectionAsync(VideoId, 200_000);
        YouTubeMusicSection? second = await client.ReadMusicSectionAsync(VideoId, 200_000);

        first.Should().BeNull();
        second.Should().BeNull();
        handler.RequestUrls.Should().HaveCount(1);

        clock.Advance(SponsorBlockClient.NoneLifetime + TimeSpan.FromSeconds(1));
        await client.ReadMusicSectionAsync(VideoId, 200_000);
        handler.RequestUrls.Should().HaveCount(2, "the cached answer expired");
    }

    [Fact]
    public async Task A_server_error_or_a_dead_service_plays_the_whole_video()
    {
        SponsorBlockClient serverError = ClientOver(
            Service(HttpStatusCode.InternalServerError),
            Clock()
        );
        SponsorBlockClient dead = ClientOver(new ThrowingHandler(), Clock());

        (await serverError.ReadMusicSectionAsync(VideoId, 200_000)).Should().BeNull();
        (await dead.ReadMusicSectionAsync(VideoId, 200_000)).Should().BeNull();
    }

    [Fact]
    public async Task Playing_a_trimmed_request_pushes_the_start_and_end_to_the_player()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = "sb1",
                Name = "SbStreamer",
                NameNormalized = "sbstreamer",
            }
        );
        db.Widgets.Add(
            new Widget
            {
                Id = PlayerWidget,
                BroadcasterId = Broadcaster,
                Name = "player",
                IsEnabled = true,
                EventSubscriptions = ["now_playing"],
            }
        );
        await db.SaveChangesAsync();
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.GetMusicWidgetId(Broadcaster).Returns(PlayerWidget);
        FakeTimeProvider clock = Clock();
        RecordingNotifier notifier = new();
        YouTubePlayerStateStore store = new(clock);
        YouTubePlayerDispatcher players = new(db, presence, notifier, store);
        RecordingHttpHandler handler = Service(HttpStatusCode.OK, Marks((0, 12.5), (180, 200)));
        YouTubeMusicProvider provider = YouTubeProviderFactory.Create(
            db: db,
            playerState: store,
            players: players,
            sponsorBlock: ClientOver(handler, clock)
        );

        bool played = await provider.PlayNowAsync(Broadcaster, VideoId);

        played.Should().BeTrue();
        YouTubePlayWidgetPayload payload = notifier
            .Sent.Should()
            .ContainSingle(s => s.EventType == "youtube.play")
            .Subject.Data.Should()
            .BeOfType<YouTubePlayWidgetPayload>()
            .Subject;
        payload.StartSeconds.Should().Be(12.5);
        payload.EndSeconds.Should().Be(180);

        TrackInfo? now = await provider.GetCurrentTrackAsync(Broadcaster);
        now.Should().NotBeNull();
        now!.DurationMs.Should().Be(167_500, "the trimmed length: 180 s minus 12.5 s");
    }

    [Fact]
    public async Task The_engine_reports_progress_and_length_of_the_trimmed_section()
    {
        IMusicProvider provider = Substitute.For<IMusicProvider, IMusicProviderPlayerGate>();
        IYouTubePlayerDispatcher dispatcher = Substitute.For<IYouTubePlayerDispatcher>();
        IYouTubePlayerStateStore store = Substitute.For<IYouTubePlayerStateStore>();
        store
            .PushedTrack(Broadcaster, VideoId)
            .Returns(new YouTubeQueuedTrack(VideoId, "t", "a", 200_000, null, 12_500, 180_000));
        YouTubePlaybackEngine engine = new(
            Broadcaster,
            () => EngineLease.Of(provider, dispatcher),
            store
        );
        List<EngineSignal> signals = [];
        engine.Reports += signal =>
        {
            signals.Add(signal);
            return Task.CompletedTask;
        };

        await engine.ReportAsync(
            new(VideoId, YouTubePlayerState.Playing, 72_500, DateTimeOffset.UnixEpoch)
        );

        EngineReport report = signals
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<EngineReport>()
            .Subject;
        report.DurationMs.Should().Be(167_500);
        report.ProgressMs.Should().Be(60_000, "72.5 s into the video is 60 s into the music");
        // The hand-over reads length minus progress: 107.5 s left, not 127.5 s.
        (report.DurationMs - report.ProgressMs)
            .Should()
            .Be(107_500);
    }
}
