// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    string TokenBucketKey
);
