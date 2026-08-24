// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// Fixed-window per-key throttle for overlay connection attempts. A legitimate browser source reconnects at
/// most every few seconds (the SDK's own backoff starts at 1s); this budget is generous for that pattern
/// while still bounding a runaway/hostile source.
/// </summary>
public sealed class OverlayConnectionThrottle : IOverlayConnectionThrottle
{
    private const int MaxAttemptsPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<
        string,
        (int Count, DateTimeOffset WindowStart)
    > _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public OverlayConnectionThrottle(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public bool TryAcquire(string key)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        (int Count, DateTimeOffset WindowStart) updated = _windows.AddOrUpdate(
            key,
            static (_, ctx) => (1, ctx),
            (_, existing, ctx) =>
                ctx - existing.WindowStart >= Window
                    ? (1, ctx)
                    : (existing.Count + 1, existing.WindowStart),
            now
        );

        return updated.Count <= MaxAttemptsPerWindow;
    }
}
