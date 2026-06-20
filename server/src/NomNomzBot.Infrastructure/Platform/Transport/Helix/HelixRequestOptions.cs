// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Typed <see cref="HttpRequestMessage.Options"/> keys the transport uses to hand per-call state to the
/// delegating handlers (the only clean seam: <see cref="HttpClient"/>'s factory builds the handler chain,
/// so per-request data travels on the message, not via DI). <see cref="TwitchAuthHeaderHandler"/> reads the
/// bearer token + client-id; <see cref="TwitchRateLimitHandler"/> reads the bucket key to feed
/// <c>ITwitchRateLimiter.Observe</c> after the response.
/// </summary>
internal static class HelixRequestOptions
{
    public static readonly HttpRequestOptionsKey<string> AccessToken = new(
        "twitch.helix.access_token"
    );
    public static readonly HttpRequestOptionsKey<string> TokenBucketKey = new(
        "twitch.helix.token_bucket_key"
    );
    public static readonly HttpRequestOptionsKey<Guid?> BroadcasterId = new(
        "twitch.helix.broadcaster_id"
    );
}
