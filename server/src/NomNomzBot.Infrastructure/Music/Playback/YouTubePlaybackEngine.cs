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
public sealed class YouTubePlaybackEngine(
    Guid broadcasterId,
    Func<EngineLease> open,
    IYouTubePlayerStateStore store
) : IPlayerReportingEngine, IPlaybackEngineProviderControls
{
    private readonly EngineSignalRelay _relay = new();
    private string? _startedUri;

    public EngineKind Kind => EngineKind.YouTube;

    public MusicProviderCapabilities Capabilities
    {
        get
        {
            using EngineLease lease = open();

            return lease.Provider.Capabilities;
        }
    }

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
        await using EngineLease lease = open();
        EngineStartResult result = await GuardFor(lease)
            .StartAsync(
                () => lease.PlayNow.PlayNowAsync(broadcasterId, item.Uri, ct),
                "YouTube could not play the video.",
                ct
            );
        if (result.Accepted)
            _startedUri = item.Uri;

        return result;
    }

    public Task PauseAsync(CancellationToken ct = default) =>
        CommandAsync(lease => lease.Provider.PauseAsync(broadcasterId, ct), ct);

    public Task ResumeAsync(CancellationToken ct = default) =>
        CommandAsync(lease => lease.Provider.PlayAsync(broadcasterId, ct), ct);

    public Task StopAsync(CancellationToken ct = default) =>
        CommandAsync(lease => lease.Dispatcher.StopAsync(broadcasterId, ct), ct);

    /// <summary>A page that reloaded mid-video gets that video back; open players are found by the replay.</summary>
    public async Task ReattachOutputAsync(bool pageIsPlaying, CancellationToken ct = default)
    {
        await using EngineLease lease = open();
        await lease.Dispatcher.ReplayCurrentAsync(broadcasterId, pageIsPlaying, ct);
    }

    /// <summary>The overlay player takes whole seconds.</summary>
    public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
        CommandAsync(lease => lease.Provider.SeekAsync(broadcasterId, positionMs / 1000, ct), ct);

    public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
        CommandAsync(lease => lease.Provider.SetVolumeAsync(broadcasterId, level, ct), ct);

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
    /// Relays one report from the overlay player. A video that ended or errored relays only
    /// <see cref="TrackEnded"/>, as the report service already treats both as the end.
    /// </summary>
    public async Task ReportAsync(YouTubePlayerReport report)
    {
        string uri = UriFor(report.VideoId);

        // An ended or failed video is the end of the track, not a pause: a failure at 30 s would read as an
        // owner pause and the director would then ignore the end.
        if (report.State is YouTubePlayerState.Ended or YouTubePlayerState.Error)
        {
            await _relay.RaiseAsync(new TrackEnded(EngineKind.YouTube, uri));
            return;
        }

        int durationMs = store.PushedTrack(broadcasterId, report.VideoId)?.DurationMs ?? 0;

        await _relay.RaiseAsync(
            new EngineReport(
                EngineKind.YouTube,
                uri,
                report.State == YouTubePlayerState.Playing,
                (int)report.PositionMs,
                durationMs
            )
        );
    }

    private async Task CommandAsync(Func<EngineLease, Task> command, CancellationToken ct)
    {
        await using EngineLease lease = open();

        await GuardFor(lease).CommandAsync(() => command(lease), ct);
    }

    private EngineCommandGuard GuardFor(EngineLease lease) =>
        new(
            EngineKind.YouTube,
            async ct =>
                await lease.PlayerGate.HasPlayerAsync(broadcasterId, ct)
                    ? null
                    : EngineBlockReason.NoOutput
        );

    /// <summary>
    /// The uri the item was started with when it is this video, so the director matches the item. A video
    /// this engine did not start (the legacy hand-over pushed it) is named by the watch url a queue row carries.
    /// </summary>
    private string UriFor(string videoId) =>
        _startedUri is not null && YouTubeVideoId.Extract(_startedUri) == videoId
            ? _startedUri
            : YouTubeMusicProvider.WatchUrl(videoId);
}
