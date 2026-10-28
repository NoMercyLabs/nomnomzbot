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

namespace NomNomzBot.Application.Platform.Services;

/// <summary>
/// The page where a streamer reaches the people who run this deployment (a suspended channel shows it). A
/// platform admin sets it; until then the <c>Platform:SupportContactUrl</c> setting stands in, else none.
/// </summary>
public interface ISupportContactService
{
    /// <summary>The support contact URL, or null when none is set.</summary>
    Task<string?> GetContactUrlAsync(CancellationToken ct = default);

    /// <summary>Sets the support contact URL (absolute http or https). Null or empty clears it.</summary>
    Task<Result<string?>> SetContactUrlAsync(string? url, CancellationToken ct = default);
}
