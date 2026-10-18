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

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The YouTube engine of one channel, over the existing provider and the overlay player's dispatcher
/// (music-playback-determinism.md section 11.2). Start is <c>PlayNowAsync</c>; Stop is <c>youtube.stop</c>;
/// Previous is not here (seek to 0 and start the item again); the queue and the hand-over are never used.
/// The overlay player pushes its state, so <see cref="ReportAsync"/> relays each report. Shuffle, repeat,
/// devices, transfer and the embedded token do not exist on this engine and answer NotSupported.
/// </summary>
public sealed class YouTubePlaybackEngine : IPlaybackEngine, IPlaybackEngineProviderControls
{
    private readonly Guid _broadcasterId;
    private readonly IMusicProvider _provider;
    private readonly IMusicProviderPlayNow _playNow;
    private readonly IYouTubePlayerDispatcher _dispatcher;
    private readonly IYouTubePlayerStateStore _store;
    private readonly EngineCommandGuard _guard;
    private readonly EngineSignalRelay _relay = new();
    private string? _startedUri;

    public YouTubePlaybackEngine(
        Guid broadcasterId,
        IMusicProvider provider,
        IMusicProviderPlayNow playNow,
        IMusicProviderPlayerGate playerGate,
        IYouTubePlayerDispatcher dispatcher,
        IYouTubePlayerStateStore store
    )
    {
        _broadcasterId = broadcasterId;
        _provider = provider;
        _playNow = playNow;
        _dispatcher = dispatcher;
        _store = store;
        _guard = new(
            EngineKind.YouTube,
            async ct =>
                await playerGate.HasPlayerAsync(broadcasterId, ct)
                    ? null
                    : EngineBlockReason.NoOutput
        );
    }

    public EngineKind Kind => EngineKind.YouTube;

    public MusicProviderCapabilities Capabilities => _provider.Capabilities;

    public event Func<EngineSignal, Task> Reports
    {
        add => _relay.Signals += value;
        remove => _relay.Signals -= value;
    }

    public async Task<EngineStartResult> StartAsync(
        PlaybackItem item,
        CancellationToken ct = default
    )
    {
        EngineStartResult result = await _guard.StartAsync(
            () => _playNow.PlayNowAsync(_broadcasterId, item.Uri, ct),
            "YouTube could not play the video.",
            ct
        );
        if (result.Accepted)
            _startedUri = item.Uri;

        return result;
    }

    public Task PauseAsync(CancellationToken ct = default) =>
        _guard.CommandAsync(() => _provider.PauseAsync(_broadcasterId, ct), ct);

    public Task ResumeAsync(CancellationToken ct = default) =>
        _guard.CommandAsync(() => _provider.PlayAsync(_broadcasterId, ct), ct);

    public Task StopAsync(CancellationToken ct = default) =>
        _guard.CommandAsync(() => _dispatcher.StopAsync(_broadcasterId, ct), ct);

    /// <summary>The overlay player takes whole seconds.</summary>
    public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
        _guard.CommandAsync(() => _provider.SeekAsync(_broadcasterId, positionMs / 1000, ct), ct);

    public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
        _guard.CommandAsync(() => _provider.SetVolumeAsync(_broadcasterId, level, ct), ct);

    public Task<EngineControlResult> SetShuffleAsync(
        bool enabled,
        CancellationToken ct = default
    ) => Task.FromResult(EngineControlResult.NotSupported);

    public Task<EngineControlResult> SetRepeatAsync(
        MusicRepeatMode mode,
        CancellationToken ct = default
    ) => Task.FromResult(EngineControlResult.NotSupported);

    public Task<EngineControlResult<IReadOnlyList<MusicDeviceInfo>>> GetDevicesAsync(
        CancellationToken ct = default
    ) => Task.FromResult(EngineControlResult<IReadOnlyList<MusicDeviceInfo>>.NotSupported);

    public Task<EngineControlResult> TransferAsync(
        string deviceId,
        bool play,
        CancellationToken ct = default
    ) => Task.FromResult(EngineControlResult.NotSupported);

    public Task<EngineControlResult<string>> GetEmbeddedTokenAsync(
        CancellationToken ct = default
    ) => Task.FromResult(EngineControlResult<string>.NotSupported);

    /// <summary>
    /// Relays one report from the overlay player. A video that ended or errored adds <see cref="TrackEnded"/>
    /// after the report, as the report service already treats both as the end.
    /// </summary>
    public async Task ReportAsync(YouTubePlayerReport report)
    {
        string uri = UriFor(report.VideoId);
        int durationMs = _store.PushedTrack(_broadcasterId, report.VideoId)?.DurationMs ?? 0;

        await _relay.RaiseAsync(
            new EngineReport(
                EngineKind.YouTube,
                uri,
                report.State == YouTubePlayerState.Playing,
                (int)report.PositionMs,
                durationMs
            )
        );

        if (report.State is YouTubePlayerState.Ended or YouTubePlayerState.Error)
            await _relay.RaiseAsync(new TrackEnded(EngineKind.YouTube, uri));
    }

    /// <summary>The uri the item was started with when it is this video, so the director matches the item.</summary>
    private string UriFor(string videoId) =>
        _startedUri is not null && YouTubeVideoId.Extract(_startedUri) == videoId
            ? _startedUri
            : videoId;
}
