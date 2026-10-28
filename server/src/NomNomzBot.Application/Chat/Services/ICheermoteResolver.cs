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

using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Resolves a cheermote (prefix + bits cheered) to the image of the tier the cheer qualified for, from the cached Helix
/// cheermotes (chat-decoration spec §3.4). Reads only cache on the hot path; returns null when the cheermote is not
/// cached or the prefix is unknown.
/// </summary>
public interface ICheermoteResolver
{
    Task<CheermoteImage?> ResolveAsync(
        Guid broadcasterId,
        string prefix,
        int bits,
        int tier,
        CancellationToken ct = default
    );
}
