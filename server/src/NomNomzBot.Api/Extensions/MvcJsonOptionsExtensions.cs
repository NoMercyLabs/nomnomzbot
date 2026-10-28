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
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Extensions;

/// <summary>
/// The JSON conventions of every REST request and response body: camelCase names, null properties left out, and
/// the owned-id converters so every id goes out as a ULID string and comes back in as a ULID or a raw Guid.
/// </summary>
public static class MvcJsonOptionsExtensions
{
    public static JsonOptions UseApiJsonConventions(this JsonOptions options)
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        OwnedIdJson.AddConverters(options.JsonSerializerOptions);
        return options;
    }
}
