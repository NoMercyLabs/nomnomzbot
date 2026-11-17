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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The one check every background seeder and sync runs before it calls Twitch for a scope-gated read: does the
/// channel's stored token hold the scope? When it does not, the caller skips quietly and the gap is recorded once
/// for the streamer's action-required inbox. Only stored scopes are read; no login or OAuth flow runs.
/// </summary>
public interface ITwitchScopeGateService
{
    /// <summary>
    /// True when the channel's Twitch token holds <paramref name="scope"/>. When it does not, records one
    /// action-required gap per channel and scope (idempotent) and returns false, so the caller makes no Twitch call.
    /// </summary>
    Task<bool> HasScopeOrRecordAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct = default
    );

    /// <summary>The failure a gated caller returns, the same shape a refused Helix pre-check returns.</summary>
    static Result MissingScope(string scope) =>
        Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);

    /// <summary>The typed twin of <see cref="MissingScope(string)"/>.</summary>
    static Result<T> MissingScope<T>(string scope) =>
        Result.Failure<T>($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
}
