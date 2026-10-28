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

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// The one list of owned-id converters. REST (MVC) and the SignalR hubs both add it, so every id goes out in the
/// same ULID form and comes back in as a ULID or a raw Guid.
/// </summary>
public static class OwnedIdJson
{
    public static void AddConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new UlidGuidJsonConverter());
        options.Converters.Add(new BindingTargetJsonConverter());
    }
}
