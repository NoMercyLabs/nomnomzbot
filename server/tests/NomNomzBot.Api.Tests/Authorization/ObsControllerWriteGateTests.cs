// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Routing;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// roles-permissions §0.3: every OBS write route is gated on a key whose seeded floor is Editor or higher
/// (<c>obs:control</c> Editor, <c>obs:control:broadcast</c> and <c>obs:config:write</c> Broadcaster), so a plain
/// Moderator is refused before any OBS command is built. Only the read routes may use a Moderator-level key.
/// The gate runs as an MVC filter, so it is proven by reading the attribute off every action of the controller.
/// </summary>
public sealed class ObsControllerWriteGateTests
{
    private static readonly string[] WriteKeys =
    [
        "obs:control",
        "obs:control:broadcast",
        "obs:config:write",
    ];

    private static readonly string[] ReadKeys = ["obs:scene:read", "obs:config:read"];

    private static List<(string Action, string Verb, string Key)> Routes()
    {
        List<(string Action, string Verb, string Key)> routes = [];
        foreach (
            MethodInfo method in typeof(ObsController).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )
        )
        {
            HttpMethodAttribute? http = method.GetCustomAttribute<HttpMethodAttribute>();
            if (http is null)
                continue;
            RequireActionAttribute? gate = method.GetCustomAttribute<RequireActionAttribute>();
            gate.Should()
                .NotBeNull($"{method.Name} is an OBS route and must carry [RequireAction]");
            routes.Add((method.Name, http.HttpMethods.Single(), gate!.ActionKey));
        }
        return routes;
    }

    [Fact]
    public void Every_non_GET_OBS_route_is_gated_on_an_Editor_or_higher_write_key()
    {
        List<(string Action, string Verb, string Key)> writes = Routes()
            .Where(r => r.Verb != "GET")
            .ToList();

        writes.Should().NotBeEmpty();
        foreach ((string action, string verb, string key) in writes)
            WriteKeys.Should().Contain(key, $"{verb} {action} writes to OBS");
    }

    [Fact]
    public void Every_GET_OBS_route_uses_a_read_key_or_the_bridge_setup_write_key()
    {
        List<(string Action, string Verb, string Key)> reads = Routes()
            .Where(r => r.Verb == "GET")
            .ToList();

        foreach ((string action, _, string key) in reads)
        {
            // GET bridge/setup reveals the bridge token, so it carries the Broadcaster write key.
            string[] allowed =
                action == nameof(ObsController.GetBridgeSetup) ? ["obs:config:write"] : ReadKeys;
            allowed.Should().Contain(key, $"GET {action}");
        }
    }

    [Fact]
    public void The_route_split_is_the_audited_one()
    {
        List<(string Action, string Verb, string Key)> routes = Routes();

        routes.Count(r => r.Verb == "GET" && r.Key == "obs:scene:read").Should().Be(14);
        routes.Count(r => r.Key == "obs:control").Should().Be(20);
        routes.Count(r => r.Key == "obs:control:broadcast").Should().Be(5);
        routes.Count(r => r.Key == "obs:config:write").Should().Be(3);
        routes.Count(r => r.Key == "obs:config:read").Should().Be(1);
    }
}
