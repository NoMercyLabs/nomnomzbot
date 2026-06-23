// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
