// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Tests.Common;

/// <summary>
/// Real-time polling for tests that wait on a background loop. It throws when the condition is not met in
/// time, so a slow machine fails at the wait with a clear message and never at an unrelated assertion.
/// </summary>
public static class TestWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public static Task UntilAsync(
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null
    ) => UntilAsync(condition, () => description, timeout);

    /// <summary>As above; <paramref name="describe"/> runs only on timeout, so it can report the state then.</summary>
    public static async Task UntilAsync(
        Func<bool> condition,
        Func<string> describe,
        TimeSpan? timeout = null
    )
    {
        TimeSpan limit = timeout ?? DefaultTimeout;
        DateTime deadline = DateTime.UtcNow + limit;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out after {limit} waiting for {describe()}.");
            await Task.Delay(10, CancellationToken.None);
        }
    }
}
