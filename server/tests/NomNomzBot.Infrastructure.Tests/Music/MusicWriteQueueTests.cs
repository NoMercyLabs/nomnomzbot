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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The write queue every favorite and banger write goes through: under the provider's budget a write goes
/// out at once; over the predicted budget it is held, told to the viewer, and sent when the stream ends.
/// </summary>
public sealed class MusicWriteQueueTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009922");
    private const string YouTubeUri = "youtube:video:abc123";
    private const string SpotifyUri = "spotify:track:t1";

    private readonly IMusicProviderManageApi _provider = Substitute.For<IMusicProviderManageApi>();
    private readonly FakeTimeProvider _clock = new(MusicWriteQueueTestKit.Start);
    private readonly MusicConfigDbFixture _config = new();

    public MusicWriteQueueTests()
    {
        _provider
            .SaveTracksAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
    }

    public void Dispose() => _config.Dispose();

    private static MusicWriteRequest Save(string provider, string uri) =>
        new(Broadcaster, provider, MusicWriteKind.Save, [uri]);

    private async Task SavesReceivedAsync(string provider, int count) =>
        await _provider
            .Received(count)
            .SaveTracksAsync(
                Broadcaster,
                provider,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task A_write_under_budget_goes_straight_to_the_provider()
    {
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock);

        Result<MusicWriteOutcome> outcome = await queue.SubmitAsync(Save("youtube", YouTubeUri));

        outcome.IsSuccess.Should().BeTrue();
        outcome.Value.Should().Be(MusicWriteOutcome.Sent);
        await _provider
            .Received(1)
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { YouTubeUri })),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_burst_past_the_predicted_budget_is_held_until_the_stream_ends()
    {
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock);
        for (int i = 0; i < 8; i++)
            (await queue.SubmitAsync(Save("youtube", $"youtube:video:v{i}")))
                .Value.Should()
                .Be(MusicWriteOutcome.Sent);

        Result<MusicWriteOutcome> ninth = await queue.SubmitAsync(
            Save("youtube", "youtube:video:v8")
        );

        ninth.Value.Should().Be(MusicWriteOutcome.Held);
        await SavesReceivedAsync("youtube", 8);

        await new MusicWriteQueueStreamOfflineHandler(queue).HandleAsync(
            new ChannelOfflineEvent
            {
                BroadcasterId = Broadcaster,
                Provider = "twitch",
                BroadcasterDisplayName = "Stoney_Eagle",
                StreamDuration = TimeSpan.FromHours(3),
            }
        );

        await SavesReceivedAsync("youtube", 9);
        await _provider
            .Received(1)
            .SaveTracksAsync(
                Broadcaster,
                "youtube",
                Arg.Is<IReadOnlyList<string>>(u => u.SequenceEqual(new[] { "youtube:video:v8" })),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_youtube_hold_never_blocks_a_spotify_write()
    {
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock);
        queue.Charge("youtube", 500);

        Result<MusicWriteOutcome> youTube = await queue.SubmitAsync(Save("youtube", YouTubeUri));
        Result<MusicWriteOutcome> spotify = await queue.SubmitAsync(Save("spotify", SpotifyUri));

        youTube.Value.Should().Be(MusicWriteOutcome.Held);
        spotify.Value.Should().Be(MusicWriteOutcome.Sent);
        await SavesReceivedAsync("youtube", 0);
        await SavesReceivedAsync("spotify", 1);
    }

    [Fact]
    public async Task Writes_stay_held_while_the_daily_quota_is_used_up_and_flush_once_it_resets()
    {
        YouTubeQuotaCooldown quota = new(_clock);
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock, quota);
        quota.CoolUntilQuotaReset();

        (await queue.SubmitAsync(Save("youtube", YouTubeUri)))
            .Value.Should()
            .Be(MusicWriteOutcome.Held);
        (await queue.FlushAsync(Broadcaster)).Should().Be(0);
        await SavesReceivedAsync("youtube", 0);

        _clock.Advance(TimeSpan.FromHours(30));

        (await queue.FlushAsync(Broadcaster)).Should().Be(1);
        await SavesReceivedAsync("youtube", 1);
    }

    [Fact]
    public async Task A_failed_write_under_budget_returns_the_provider_failure_and_holds_nothing()
    {
        _provider
            .SaveTracksAsync(
                Broadcaster,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("down", "SERVICE_UNAVAILABLE"));
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock);

        Result<MusicWriteOutcome> outcome = await queue.SubmitAsync(Save("spotify", SpotifyUri));

        outcome.IsFailure.Should().BeTrue();
        outcome.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        (await queue.FlushAsync(Broadcaster)).Should().Be(0);
    }

    [Fact]
    public async Task Banger_tells_the_viewer_the_song_is_held_and_saves_it_after_the_stream()
    {
        MusicWriteQueue queue = MusicWriteQueueTestKit.Over(_provider, _clock);
        queue.Charge("youtube", 500);
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Test Anthem",
                    Artist: "Test Artist",
                    Album: null,
                    ImageUrl: null,
                    DurationMs: 200_000,
                    ProgressMs: 1_000,
                    IsPlaying: true,
                    Volume: 50,
                    RequestedBy: null,
                    Provider: "youtube",
                    TrackUri: YouTubeUri
                )
            );
        BangerBuiltin banger = new(
            music,
            _provider,
            queue,
            _config.Service,
            TestBuiltinComposer.Create(),
            NullLogger<BangerBuiltin>.Instance
        );
        BuiltinCommandContext context = new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer1",
            RoleLevel = 0,
            Personality = PersonalityTone.Sassy,
        };

        Result<string> reply = await banger.ExecuteAsync(context);

        List<string> held = [];
        foreach (
            string template in ToneTemplateCatalog.Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Banger.Key,
                BuiltinResponseSlots.Banger.Held
            )
        )
            held.Add(
                await TestTemplateResolver
                    .Create()
                    .ResolveAsync(
                        template,
                        new Dictionary<string, string>
                        {
                            ["user"] = "Viewer1",
                            ["track.name"] = "Test Anthem",
                            ["track.artist"] = "Test Artist",
                            ["playlist.name"] = "your YouTube likes",
                        },
                        null
                    )
            );
        held.Should().Contain(reply.Value);
        await SavesReceivedAsync("youtube", 0);

        (await queue.FlushAsync(Broadcaster)).Should().Be(1);
        await SavesReceivedAsync("youtube", 1);
    }
}
