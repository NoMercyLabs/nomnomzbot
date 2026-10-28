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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// One immutable bearer context for a single Helix call (twitch-helix.md §3.5).
/// Carries the decrypted access token, the identity that owns it, and a stable bucket
/// key for the adaptive rate limiter. The raw <see cref="AccessToken"/> is never logged.
/// </summary>
public sealed record TwitchAccessContext(
    string AccessToken,
    Guid? BroadcasterId,
    string ServiceName,
    string TokenBucketKey,
    Guid? ConnectionId = null
);
