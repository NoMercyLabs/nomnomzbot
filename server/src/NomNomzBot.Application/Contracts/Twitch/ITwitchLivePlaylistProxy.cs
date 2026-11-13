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
/// Fetches one Twitch live HLS playlist for the dashboard's player, which cannot read it straight from Twitch
/// (no CORS). The address arrives in the player's proxy form: base64url, no padding. Only Twitch's own
/// playlist hosts are fetched; every nested playlist in the answer is rewritten into the same proxy form, and
/// video segments stay absolute on Twitch's CDN.
/// </summary>
public interface ITwitchLivePlaylistProxy
{
    /// <summary>The <c>Result.ErrorCode</c> for an address that does not decode or is off the allowlist.</summary>
    public const string InvalidAddress = "invalid_address";

    /// <summary>
    /// The rewritten playlist text. Fails with <see cref="InvalidAddress"/>, <see cref="TwitchErrorCodes.NotFound"/>
    /// (the channel is offline), or <see cref="TwitchErrorCodes.Transport"/> (any other upstream failure).
    /// </summary>
    Task<Result<string>> FetchAsync(string encodedAddress, CancellationToken cancellationToken);
}
