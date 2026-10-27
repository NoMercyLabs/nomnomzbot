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

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>The one stored JSON shape of a <see cref="ReplyDelivery"/>: <c>{"target":"overlay","html":true}</c>.</summary>
public static class ReplyDeliveryJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ReplyDelivery delivery) =>
        JsonSerializer.Serialize(delivery, Options);

    /// <summary>Reads a stored value; null, blank or unreadable text means the default (chat only).</summary>
    public static ReplyDelivery? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ReplyDelivery>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
