// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Shared test parts that run the REAL YouTube engine under a playback director registry, so a test builds
/// the registry first and hands that one registry to the music service, the report service and the reconciler.
/// </summary>
internal static class YouTubeDirectorKit
{
    /// <summary>
    /// The real YouTube engine, delegating everything and recording each start the director sends it. This
    /// separates the director's pushes from the legacy hand-over's pushes, which both reach the notifier.
    /// </summary>
    internal sealed class StartRecordingEngine(YouTubePlaybackEngine inner)
        : IPlayerReportingEngine,
            IPlaybackEngineProviderControls
    {
        public List<string> StartedVideoIds { get; } = [];

        public EngineKind Kind => inner.Kind;

        public MusicProviderCapabilities Capabilities => inner.Capabilities;

        public event Func<EngineSignal, Task> Reports
        {
            add => inner.Reports += value;
            remove => inner.Reports -= value;
        }

        public Task<EngineStartResult> StartAsync(PlaybackItem item, CancellationToken ct = default)
        {
            StartedVideoIds.Add(YouTubeVideoId.Extract(item.Uri)!);

            return inner.StartAsync(item, ct);
        }

        public Task ReportAsync(YouTubePlayerReport report) => inner.ReportAsync(report);

        public Task PauseAsync(CancellationToken ct = default) => inner.PauseAsync(ct);

        public Task ResumeAsync(CancellationToken ct = default) => inner.ResumeAsync(ct);

        public Task StopAsync(CancellationToken ct = default) => inner.StopAsync(ct);

        public Task ReattachOutputAsync(bool pageIsPlaying, CancellationToken ct = default) =>
            inner.ReattachOutputAsync(pageIsPlaying, ct);

        public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
            inner.SeekAsync(positionMs, ct);

        public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
            inner.SetVolumeAsync(level, ct);

        public Task<EngineControlResult> SetShuffleAsync(
            bool enabled,
            CancellationToken ct = default
        ) => inner.SetShuffleAsync(enabled, ct);

        public Task<EngineControlResult> SetRepeatAsync(
            MusicRepeatMode mode,
            CancellationToken ct = default
        ) => inner.SetRepeatAsync(mode, ct);

        public Task<EngineControlResult<IReadOnlyList<MusicDeviceInfo>>> GetDevicesAsync(
            CancellationToken ct = default
        ) => inner.GetDevicesAsync(ct);

        public Task<EngineControlResult> TransferAsync(
            string deviceId,
            bool play,
            CancellationToken ct = default
        ) => inner.TransferAsync(deviceId, play, ct);

        public Task<EngineControlResult<string>> GetEmbeddedTokenAsync(
            CancellationToken ct = default
        ) => inner.GetEmbeddedTokenAsync(ct);
    }

    /// <summary>The recording YouTube engine over the real provider and dispatcher, next to a fake Spotify engine.</summary>
    internal sealed class RealYouTubeEngineFactory(
        YouTubeMusicProvider provider,
        YouTubePlayerDispatcher players,
        YouTubePlayerStateStore store
    ) : IPlaybackEngineFactory
    {
        public StartRecordingEngine? YouTube { get; private set; }

        public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
        {
            YouTube = new(
                new YouTubePlaybackEngine(
                    broadcasterId,
                    () => EngineLease.Of(provider, players),
                    store
                )
            );

            return [new PlaybackDirectorTestKit.FakeEngine(EngineKind.Spotify), YouTube];
        }
    }

    /// <summary>The provider is built before the registry that needs it; this hands its inputs on once it exists.</summary>
    internal sealed class LateSink : IPlaybackInputSink
    {
        public IPlaybackInputSink? Inner { get; set; }

        public Task SendAsync(
            Guid broadcasterId,
            PlaybackInput input,
            CancellationToken ct = default
        ) => Inner!.SendAsync(broadcasterId, input, ct);
    }
}
