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
