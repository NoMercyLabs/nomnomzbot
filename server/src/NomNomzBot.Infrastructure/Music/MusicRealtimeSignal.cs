// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Music;

namespace NomNomzBot.Infrastructure.Music;

/// <inheritdoc />
/// <remarks>
/// A latching gate, not an event. <see cref="Nudge"/> arriving while the poller is mid-pass must not be
/// lost — that is precisely the frame that says the track changed — so the signal stays raised until a
/// waiter consumes it, and the next <see cref="WaitForNudgeAsync"/> returns immediately.
/// </remarks>
public sealed class MusicRealtimeSignal : IMusicRealtimeSignal, IDisposable
{
    // Starts empty, caps at one: many frames arriving between passes are still just "look again once".
    private readonly SemaphoreSlim _gate = new(0, 1);

    /// <summary>
    /// The channels nudged since the last pass. The poller sweeps every connected channel anyway, so this is
    /// diagnostic rather than a work list — it answers "is the socket actually feeding us?" without turning
    /// every frame into a log line.
    /// </summary>
    private readonly HashSet<Guid> _nudged = [];
    private readonly Lock _nudgedLock = new();

    /// <inheritdoc />
    public void Nudge(Guid broadcasterId)
    {
        lock (_nudgedLock)
        {
            _nudged.Add(broadcasterId);
        }

        // Release throws once the count is at its maximum; a second nudge before anyone waits is a no-op by
        // design, not an error.
        try
        {
            _gate.Release();
        }
        catch (SemaphoreFullException) { }
    }

    /// <inheritdoc />
    public Task WaitForNudgeAsync(CancellationToken cancellationToken) =>
        _gate.WaitAsync(cancellationToken);

    /// <summary>Channels nudged since this was last called, then cleared.</summary>
    public IReadOnlyCollection<Guid> DrainNudged()
    {
        lock (_nudgedLock)
        {
            Guid[] drained = [.. _nudged];
            _nudged.Clear();
            return drained;
        }
    }

    public void Dispose() => _gate.Dispose();
}
