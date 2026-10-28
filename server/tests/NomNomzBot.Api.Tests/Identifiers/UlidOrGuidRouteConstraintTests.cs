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

using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// The <c>{id:guid}</c> route constraint (swapped for <see cref="UlidOrGuidRouteConstraint"/>). A 26-char ULID
/// path segment matches — without the swap the built-in constraint would 404 it before model binding — as does a
/// raw Guid string and an actual Guid route value (URL generation); anything else is rejected.
/// </summary>
public sealed class UlidOrGuidRouteConstraintTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly UlidOrGuidRouteConstraint Constraint = new();

    private static bool Match(object? value) =>
        Constraint.Match(
            httpContext: null,
            route: null,
            routeKey: "id",
            values: new() { ["id"] = value },
            routeDirection: RouteDirection.IncomingRequest
        );

    [Fact]
    public void Matches_a_ulid_string() => Match(GuidUlidCodec.Encode(KnownId)).Should().BeTrue();

    [Fact]
    public void Matches_a_raw_guid_string() => Match(KnownId.ToString()).Should().BeTrue();

    [Fact]
    public void Matches_an_actual_guid_value_for_url_generation() =>
        Match(KnownId).Should().BeTrue();

    [Fact]
    public void Rejects_a_non_id_string() => Match("not-an-id").Should().BeFalse();

    [Fact]
    public void Rejects_a_null_value() => Match(null).Should().BeFalse();

    [Fact]
    public void Rejects_a_missing_route_key() =>
        Constraint
            .Match(null, null, "id", new(), RouteDirection.IncomingRequest)
            .Should()
            .BeFalse();
}
