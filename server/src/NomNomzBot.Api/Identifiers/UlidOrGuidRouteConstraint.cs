// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// Route constraint that accepts a ULID string OR a raw Guid string, registered as the <c>guid</c> inline
/// constraint (<c>{id:guid}</c>) so every owned-id route matches its ULID wire form as well as a raw Guid. Without
/// it, the framework's built-in <c>guid</c> constraint 404s a 26-char ULID before model binding ever runs. On URL
/// generation the route value is the actual <see cref="Guid"/>, which is always accepted.
/// </summary>
public sealed class UlidOrGuidRouteConstraint : IRouteConstraint
{
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection
    )
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(routeKey, out object? routeValue) || routeValue is null)
            return false;

        if (routeValue is Guid)
            return true;

        string? asString = Convert.ToString(routeValue, CultureInfo.InvariantCulture);
        return GuidUlidCodec.TryDecode(asString, out _);
    }
}
