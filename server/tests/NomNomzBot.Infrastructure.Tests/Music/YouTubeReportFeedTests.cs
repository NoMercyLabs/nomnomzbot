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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-PD-4b2: the overlay player's report reaches the YouTube engine through <see cref="IEngineFeed"/>, and the
/// director decides. Runs the real report service, state store, registry, director and YouTube engine over a
/// substitute provider; the queue rows, the engine start calls and the published events are what is asserted.
/// </summary>
public sealed class YouTubeReportFeedTests
{
    private const string First = "dQw4w9WgXcQ";
    private const string Second = "9bZkp7q19f0";
    private static readonly Guid Widget = Guid.Parse("0192c000-0000-7000-8000-00000000d0aa");

    private readonly SongRequestQueueStore _queues = new();
    private readonly RecordingBus _bus = new();
    private readonly YouTubePlayerStateStore _store = new(new FakeTimeProvider());
    private readonly IYouTubePlayerDispatcher _dispatcher =
        Substitute.For<IYouTubePlayerDispatcher>();
    private readonly IMusicProvider _provider = Substitute.For<
        IMusicProvider,
        IMusicProviderPlayNow,
        IMusicProviderPlayerGate
    >();
    private readonly CountingFactory _engines;
    private readonly PlaybackDirectorRegistry _registry;
    private readonly YouTubePlayerReportService _reports;

    public YouTubeReportFeedTests()
    {
        _provider.Capabilities.Returns(MusicProviderCapabilities.PlaybackControl);
        ((IMusicProviderPlayerGate)_provider)
            .HasPlayerAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(true);
        PlayNow
            .PlayNowAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string id = YouTubeVideoId.Extract(call.ArgAt<string>(1))!;
                _store.MarkPushed(Channel, new(id, $"Title {id}", "Artist"));

                return true;
            });
        _dispatcher
            .FindPlayersAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>>([Widget]));
        _engines = new(_provider, _dispatcher, _store);
        _registry = new(_queues, _engines, _bus, TimeProvider.System);
        _reports = new(
            _store,
            _dispatcher,
            _bus,
            _registry,
            new PlayOnceResumeTracker(),
            NullLogger<YouTubePlayerReportService>.Instance
        );
    }

    private IMusicProviderPlayNow PlayNow => (IMusicProviderPlayNow)_provider;

    private static string Watch(string id) => $"https://www.youtube.com/watch?v={id}";

    private void Request(string id, string owner = "alice") =>
        _queues
            .GetOrCreate(Channel.ToString())
            .Enqueue(
                owner,
                new(Watch(id), $"Title {id}", "Artist", null, 200_000, owner, 0, null, id)
            );

    private List<string> Queued() =>
        [.. _queues.GetOrCreate(Channel.ToString()).GetSnapshot().Select(e => e.Item.TrackUri)];

    private int PlayNowOf(string id) =>
        PlayNow
            .ReceivedCalls()
            .Count(call =>
                call.GetMethodInfo().Name == nameof(IMusicProviderPlayNow.PlayNowAsync)
                && (string?)call.GetArguments()[1] == Watch(id)
            );

    private async Task ReportAsync(string id, string state)
    {
        Result result = await _reports.ReportAsync(Channel, Widget, id, state, 1_000);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task One_error_keeps_the_head_and_the_output_coming_back_retries_it_once_more()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());
        PlayNowOf(First).Should().Be(1);

        await ReportAsync(First, "error");

        Queued().Should().Equal(Watch(First), Watch(Second));
        PlayNowOf(Second).Should().Be(0, "one failure only blocks the head");

        await _registry.SendAsync(Channel, new OutputAttached());

        PlayNowOf(First).Should().Be(2, "the head is retried once");
        Queued().Should().Equal(Watch(First), Watch(Second));
    }

    [Fact]
    public async Task A_second_error_on_the_head_drops_it_and_starts_the_next_with_one_failure_notice()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());
        await ReportAsync(First, "error");
        await _registry.SendAsync(Channel, new OutputAttached());

        await ReportAsync(First, "error");

        Queued().Should().Equal([Watch(Second)], "the failed head left the queue");
        PlayNowOf(Second).Should().BeGreaterThanOrEqualTo(1);
        SongPlaybackFailedEvent failed = _bus
            .Published.OfType<SongPlaybackFailedEvent>()
            .Should()
            .ContainSingle("one failure episode reaches chat once")
            .Subject;
        failed.BroadcasterId.Should().Be(Channel);
        failed.TrackUri.Should().Be(Watch(First));
        failed.TrackName.Should().Be($"Title {First}");
    }

    [Fact]
    public async Task An_ended_head_starts_the_next_request_in_the_engine_and_the_event_is_marked_fed()
    {
        Request(First);
        Request(Second, "bob");
        await _registry.SendAsync(Channel, new RequestAdded());
        await ReportAsync(First, "playing");
        Queued().Should().Equal(Watch(Second));

        await ReportAsync(First, "ended");

        PlayNowOf(Second).Should().Be(1, "the director starts the next request exactly once");
        _bus.Published.OfType<PlaybackStateChangedEvent>()
            .Should()
            .OnlyContain(e => e.FedToEngine, "the reconciler must not forward it a second time");
    }

    [Fact]
    public async Task A_report_for_a_channel_with_no_director_changes_nothing_and_creates_no_engines()
    {
        Request(First);

        Func<Task<Result>> report = () => _reports.ReportAsync(Channel, Widget, First, "error", 1);

        await report.Should().NotThrowAsync();
        _engines.CreateCount.Should().Be(0, "a report never creates a director or its engines");
        Queued().Should().Equal(Watch(First));
        PlayNowOf(First).Should().Be(0);
    }

    /// <summary>A real YouTube engine over the substitute provider next to a fake Spotify engine.</summary>
    private sealed class CountingFactory(
        IMusicProvider provider,
        IYouTubePlayerDispatcher dispatcher,
        IYouTubePlayerStateStore store
    ) : IPlaybackEngineFactory
    {
        public int CreateCount { get; private set; }

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            CreateCount++;

            return
            [
                new FakeEngine(EngineKind.Spotify),
                new YouTubePlaybackEngine(
                    broadcasterId,
                    () => EngineLease.Of(provider, dispatcher),
                    store
                ),
            ];
        }
    }
}
