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

using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music.Pipeline;

/// <summary>
/// Decides whether the channel's music provider can be read at all, for the music picker kinds. The music service
/// answers a playback device or track query with an empty list when it cannot reach the provider, so the pickers ask
/// first and report the reason instead of an empty list.
/// </summary>
internal static class MusicOptionSource
{
    /// <summary>
    /// Why the music provider cannot be read for <paramref name="broadcasterKey"/>, or null when it is connected and its
    /// connection looks healthy.
    /// </summary>
    public static async Task<string?> UnavailableReasonAsync(
        IMusicService music,
        string broadcasterKey,
        CancellationToken ct
    )
    {
        string? provider = await music.GetActiveProviderKeyAsync(broadcasterKey, ct);
        if (provider is null)
            return "No music provider is connected.";

        string? status = await music.GetActiveProviderAuthStatusAsync(broadcasterKey, ct);
        return status switch
        {
            null or "" => null,
            "needs_reauth" => "The music provider connection has expired. Connect it again.",
            "forbidden" => "The music provider refused access. Check the granted permissions.",
            _ => $"The music provider connection reports: {status}.",
        };
    }
}
