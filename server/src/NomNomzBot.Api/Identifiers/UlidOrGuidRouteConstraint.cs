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
