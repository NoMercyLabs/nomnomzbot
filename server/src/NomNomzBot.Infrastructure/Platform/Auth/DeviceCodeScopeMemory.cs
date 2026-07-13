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

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Remembers the exact scope set each in-flight Device Code Flow was REQUESTED with, keyed by device code, so
/// the token-exchange poll re-sends that same set — critical for the additive scope re-grant, whose widened
/// set (granted ∪ missing) is minted at the start request but must be re-sent on the poll.
/// <para>
/// This is process-wide shared state and MUST be a singleton: the start request and the device polls are
/// SEPARATE HTTP requests, so a per-request (scoped) owner would always find an empty map at poll time and fall
/// back to the narrower base scopes — which is exactly the bug that stopped a re-grant's extra scopes from ever
/// landing. Mirrors <see cref="DeviceCodePollThrottle"/> (also a singleton for the same reason).
/// </para>
/// </summary>
public sealed class DeviceCodeScopeMemory
{
    private readonly ConcurrentDictionary<string, string[]> _byDeviceCode = new();

    /// <summary>Records the scopes <paramref name="deviceCode"/> was authorized for (widened for a re-grant).</summary>
    public void Remember(string deviceCode, IReadOnlyList<string> scopes) =>
        _byDeviceCode[deviceCode] = [.. scopes];

    /// <summary>The scopes the code was requested with, or null when unknown (never started here, or already forgotten).</summary>
    public string[]? Recall(string deviceCode) =>
        _byDeviceCode.TryGetValue(deviceCode, out string[]? scopes) ? scopes : null;

    /// <summary>Drops a code's remembered scopes once its flow reaches a terminal state, so abandoned codes don't accumulate.</summary>
    public void Forget(string deviceCode) => _byDeviceCode.TryRemove(deviceCode, out _);
}
