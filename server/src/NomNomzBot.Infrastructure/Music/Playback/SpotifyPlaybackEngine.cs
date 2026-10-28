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

using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The Spotify engine of one channel, over the existing provider (music-playback-determinism.md section 11.2).
/// Start is <c>StartTrackAsync</c>; Stop is a pause; Previous is not here (the director seeks to 0 and starts the
/// item again, and the provider's previous-track endpoint is never called); Skip moves Spotify's own queue on. Spotify has
/// no push, so <see cref="ObserveAsync"/> turns each poll into a report and derives the track end.
/// Every command opens an <see cref="EngineLease"/> and disposes it after, so the scoped provider never outlives it.
/// </summary>
public sealed class SpotifyPlaybackEngine(
    Guid broadcasterId,
    Func<EngineLease> open,
    TimeProvider? time = null
) : IPolledPlaybackEngine, IPlaybackEngineProviderControls
{
    /// <summary>A track whose poll showed it within this long of its end counts as ended when it is replaced.</summary>
    private const int EndWindowMs = 10_000;

    /// <summary>
    /// After Spotify answers "no active device", the engine answers the same block itself for this long. The
    /// poller asks the director to start the head every few seconds; without this pause each ask is a call
    /// Spotify refuses again (live 2026-10-20: a 404 every 5 s). The block still shows once as the state.
    /// </summary>
    private static readonly TimeSpan NoDeviceBackoff = TimeSpan.FromSeconds(30);

    private readonly EngineSignalRelay _relay = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private DateTimeOffset _noDeviceUntil;
    private TrackInfo? _lastPoll;

    /// <summary>Runs a start or line-up through the guard, holding back while Spotify has no device.</summary>
    private async Task<EngineStartResult> GuardedStartAsync(
        EngineLease lease,
        Func<Task<bool>> start,
        string refusedReason,
        CancellationToken ct
    )
    {
        if (_time.GetUtcNow() < _noDeviceUntil)
            return EngineStartResult.BlockedBy(EngineBlockReason.NoOutput);

        EngineStartResult result = await GuardFor(lease).StartAsync(start, refusedReason, ct);
        if (result.Block == EngineBlockReason.NoOutput)
            _noDeviceUntil = _time.GetUtcNow() + NoDeviceBackoff;

        return result;
    }

    public void ForgetOutputBlock() => _noDeviceUntil = default;

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

        return await GuardedStartAsync(
            lease,
            async () =>
                await HoldsAsync(lease, item, ct)
                    ? await SkipHeldAsync(lease, ct)
                    : await lease.StartTrack.StartTrackAsync(broadcasterId, item.Uri, ct),
            "Spotify could not start the track.",
            ct
        );
    }

    /// <summary>
    /// Whether Spotify's own queue already holds the item. The director's memory of what it sent dies with the
    /// process, so after a restart or a blue/green overlap the player is the only witness. An unanswerable read
    /// counts as "not held": a viewer's request is never dropped on a failed read.
    /// </summary>
    private async Task<bool> HoldsAsync(EngineLease lease, PlaybackItem item, CancellationToken ct)
    {
        IReadOnlyList<TrackInfo>? held = await lease.Provider.GetQueueAsync(broadcasterId, ct);

        return held?.Any(t =>
                string.Equals(t.TrackUri, item.Uri, StringComparison.OrdinalIgnoreCase)
            ) == true;
    }

    /// <summary>The item is already in Spotify's queue: moving on plays it, and no second copy is added.</summary>
    private async Task<bool> SkipHeldAsync(EngineLease lease, CancellationToken ct)
    {
        await lease.Provider.SkipAsync(broadcasterId, ct);

        return true;
    }

    /// <summary>A started or queued track sits in Spotify's own queue until it plays.</summary>
    public bool LinesUpInItsOwnQueue => true;

    /// <summary>Spotify's own queue plays the item right after the owner's current track.</summary>
    public async Task<EngineStartResult> QueueNextAsync(
        PlaybackItem item,
        CancellationToken ct = default
    )
    {
        await using EngineLease lease = open();

        return await GuardedStartAsync(
            lease,
            async () =>
                await HoldsAsync(lease, item, ct)
                || await lease.Provider.AddToQueueAsync(broadcasterId, item.Uri, ct),
            "Spotify could not queue the track.",
            ct
        );
    }

    public Task PauseAsync(CancellationToken ct = default) =>
        CommandAsync(provider => provider.PauseAsync(broadcasterId, ct), ct);

    public Task ResumeAsync(CancellationToken ct = default) =>
        CommandAsync(provider => provider.PlayAsync(broadcasterId, ct), ct);

    /// <summary>Spotify has no stop: pausing is what forgetting the track means here.</summary>
    public Task StopAsync(CancellationToken ct = default) => PauseAsync(ct);

    /// <summary>Spotify's own next track plays: a lined-up request, else the owner's playlist goes on.</summary>
    public Task SkipAsync(CancellationToken ct = default) =>
        CommandAsync(provider => provider.SkipAsync(broadcasterId, ct), ct);

    public async Task<bool> CanStartAsync(CancellationToken ct = default)
    {
        await using EngineLease lease = open();

        return await GuardFor(lease).IsReadyAsync(ct);
    }

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

        EngineControlResult result = await GuardFor(lease)
            .ControlAsync(
                () => lease.Provider.TransferPlaybackAsync(broadcasterId, deviceId, play, ct),
                ct
            );
        if (result.Outcome == EngineControlOutcome.Done)
            ForgetOutputBlock();

        return result;
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
                    polled.DurationMs,
                    polled.DeviceId
                )
            );
    }

    /// <summary>
    /// One read of what Spotify plays now. It is the baseline of the next poll, and it raises no signal: the
    /// director that asked holds its turn.
    /// </summary>
    public async Task<EngineReport?> ProbeAsync(CancellationToken ct = default)
    {
        await using EngineLease lease = open();

        TrackInfo? current = await lease.Provider.GetCurrentTrackAsync(broadcasterId, ct);
        _lastPoll = current;

        return current is null
            ? null
            : new EngineReport(
                EngineKind.Spotify,
                current.TrackUri,
                current.IsPlaying,
                current.ProgressMs,
                current.DurationMs,
                current.DeviceId
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
