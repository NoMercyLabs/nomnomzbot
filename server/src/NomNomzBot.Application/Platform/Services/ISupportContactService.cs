// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
