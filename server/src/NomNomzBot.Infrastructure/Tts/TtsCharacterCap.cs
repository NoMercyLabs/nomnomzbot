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
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>The one rule for how many characters a spoken line may have.</summary>
internal static class TtsCharacterCap
{
    /// <summary>The cap when the streamer set none.</summary>
    public const int DefaultCharacters = 500;

    /// <summary>
    /// The effective character cap: the STRICTER of the streamer's own setting and the plan's
    /// tts_max_characters tier limit (monetization-billing §3.3; -1 / self-host = no tier clamp).
    /// </summary>
    public static async Task<int> ResolveAsync(
        Guid broadcasterId,
        TtsConfigDto config,
        IBillingTierService tiers,
        CancellationToken ct
    )
    {
        int cap = config.MaxCharacters > 0 ? config.MaxCharacters : DefaultCharacters;
        Result<long> tierCap = await tiers.GetLimitAsync(broadcasterId, "tts_max_characters", ct);
        if (tierCap is { IsSuccess: true, Value: >= 0 })
            cap = (int)Math.Min(cap, tierCap.Value);
        return cap;
    }
}
