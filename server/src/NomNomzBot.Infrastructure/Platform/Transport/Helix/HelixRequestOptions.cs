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
    public static readonly HttpRequestOptionsKey<string> ClientId = new("twitch.helix.client_id");
    public static readonly HttpRequestOptionsKey<string> TokenBucketKey = new(
        "twitch.helix.token_bucket_key"
    );
    public static readonly HttpRequestOptionsKey<Guid?> BroadcasterId = new(
        "twitch.helix.broadcaster_id"
    );
}
