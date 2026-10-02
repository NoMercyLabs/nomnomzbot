// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The JSON form overlay payloads take when this folder serializes them itself: camelCase, the same names the
/// SignalR hub writes for a live push. A widget reads one set of field names whether the payload came live, from
/// the overlay feed's JSON string, or from a replay of a stored capture.
/// </summary>
internal static class OverlayWireJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
