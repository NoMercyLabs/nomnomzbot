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
