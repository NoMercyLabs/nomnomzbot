// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The Spotify engine of one channel, over the existing provider (music-playback-determinism.md section 11.2).
/// Start is <c>StartTrackAsync</c>; Stop is a pause; Previous is not here (the director seeks to 0 and starts the
/// item again, and the provider's previous-track endpoint is never called); the queue is never used. Spotify has
/// no push, so <see cref="ObserveAsync"/> turns each poll into a report and derives the track end.
/// Every command opens an <see cref="EngineLease"/> and disposes it after, so the scoped provider never outlives it.
/// </summary>
public sealed class SpotifyPlaybackEngine(Guid broadcasterId, Func<EngineLease> open)
    : IPlaybackEngine,
        IPlaybackEngineProviderControls
{
    /// <summary>A track whose poll showed it within this long of its end counts as ended when it is replaced.</summary>
    private const int EndWindowMs = 10_000;

    private readonly EngineSignalRelay _relay = new();
    private TrackInfo? _lastPoll;

    public EngineKind Kind => EngineKind.Spotify;

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

        return await GuardFor(lease)
            .StartAsync(
                () => lease.StartTrack.StartTrackAsync(broadcasterId, item.Uri, ct),
                "Spotify could not start the track.",
                ct
            );
    }

    public Task PauseAsync(CancellationToken ct = default) =>
        CommandAsync(provider => provider.PauseAsync(broadcasterId, ct), ct);

    public Task ResumeAsync(CancellationToken ct = default) =>
        CommandAsync(provider => provider.PlayAsync(broadcasterId, ct), ct);

    /// <summary>Spotify has no stop: pausing is what forgetting the track means here.</summary>
    public Task StopAsync(CancellationToken ct = default) => PauseAsync(ct);

    /// <summary>Spotify takes whole seconds.</summary>
    public Task SeekAsync(int positionMs, CancellationToken ct = default) =>
        CommandAsync(provider => provider.SeekAsync(broadcasterId, positionMs / 1000, ct), ct);

    public Task SetVolumeAsync(int level, CancellationToken ct = default) =>
        CommandAsync(provider => provider.SetVolumeAsync(broadcasterId, level, ct), ct);

    public async Task<EngineControlResult> SetShuffleAsync(
        bool enabled,
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardFor(lease)
            .ControlAsync(() => lease.Provider.SetShuffleAsync(broadcasterId, enabled, ct), ct);
    }

    public async Task<EngineControlResult> SetRepeatAsync(
        MusicRepeatMode mode,
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardFor(lease)
            .ControlAsync(() => lease.Provider.SetRepeatAsync(broadcasterId, mode, ct), ct);
    }

    public async Task<EngineControlResult<IReadOnlyList<MusicDeviceInfo>>> GetDevicesAsync(
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardFor(lease)
            .ControlAsync(() => lease.Provider.GetDevicesAsync(broadcasterId, ct), ct);
    }

    public async Task<EngineControlResult> TransferAsync(
        string deviceId,
        bool play,
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardFor(lease)
            .ControlAsync(
                () => lease.Provider.TransferPlaybackAsync(broadcasterId, deviceId, play, ct),
                ct
            );
    }

    public async Task<EngineControlResult<string>> GetEmbeddedTokenAsync(
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardFor(lease)
            .ControlAsync(
                async () =>
                    await lease.Provider.GetEmbeddedPlaybackTokenAsync(broadcasterId, ct)
                    ?? throw new MusicAuthenticationFailedException(lease.Provider.Provider),
                ct
            );
    }

    /// <summary>
    /// Feeds one poll result in. Emits <see cref="TrackEnded"/> first when the previous poll was within
    /// <see cref="EndWindowMs"/> of the end and the track is now gone, replaced or restarted; then the report.
    /// A track replaced mid-song is not an end: the report alone tells the director what plays now.
    /// </summary>
    public async Task ObserveAsync(TrackInfo? polled)
    {
        TrackInfo? previous = _lastPoll;
        _lastPoll = polled;

        if (previous is not null && EndedSince(previous, polled))
            await _relay.RaiseAsync(new TrackEnded(EngineKind.Spotify, previous.TrackUri));

        if (polled is not null)
            await _relay.RaiseAsync(
                new EngineReport(
                    EngineKind.Spotify,
                    polled.TrackUri,
                    polled.IsPlaying,
                    polled.ProgressMs,
                    polled.DurationMs
                )
            );
    }

    private async Task CommandAsync(Func<IMusicProvider, Task> command, CancellationToken ct)
    {
        await using EngineLease lease = open();

        await GuardFor(lease).CommandAsync(() => command(lease.Provider), ct);
    }

    private EngineCommandGuard GuardFor(EngineLease lease) =>
        new(
            EngineKind.Spotify,
            async ct =>
                await lease.ConnectionGate.IsConnectedAsync(broadcasterId, ct)
                    ? null
                    : EngineBlockReason.NotConnected
        );

    private static bool EndedSince(TrackInfo last, TrackInfo? now)
    {
        bool nearEnd = last.DurationMs > 0 && last.DurationMs - last.ProgressMs <= EndWindowMs;

        return nearEnd
            && (now is null || now.TrackUri != last.TrackUri || now.ProgressMs < last.ProgressMs);
    }
}
