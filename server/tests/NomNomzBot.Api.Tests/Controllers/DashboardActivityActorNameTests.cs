// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the dashboard activity feed resolves an actor's name from the event payload, so an event whose actor
/// has no local User row (a follower or raider who never chatted — the common case, since the actor→User link is
/// a deferred batch backfill) shows their real name instead of the "— followed" dash the home widget rendered.
/// Payload shapes are the real ones stored by the modern EventSub projection (actorDisplay/actorLogin, raids add
/// fromDisplayName/fromLogin) and by the legacy importer (user/user.name).
/// </summary>
public sealed class DashboardActivityActorNameTests
{
    [Theory]
    // Modern follow — the display name is preferred over the login.
    [InlineData(
        "{\"userId\":\"405277251\",\"userLogin\":\"r2_adhd2\",\"actorLogin\":\"r2_adhd2\",\"actorDisplay\":\"R2_ADHD2\",\"userDisplayName\":\"R2_ADHD2\",\"actorTwitchUserId\":\"405277251\"}",
        "R2_ADHD2"
    )]
    // Modern raid — the raider resolves from actorDisplay too (the field the projection always writes).
    [InlineData(
        "{\"fromLogin\":\"manadono\",\"actorLogin\":\"manadono\",\"fromUserId\":\"120307729\",\"viewerCount\":3,\"actorDisplay\":\"Manadono\",\"fromDisplayName\":\"Manadono\",\"actorTwitchUserId\":\"120307729\"}",
        "Manadono"
    )]
    // Legacy imported event — the actor lived under "user".
    [InlineData(
        "{\"user\":\"R2_ADHD2\",\"user.id\":\"405277251\",\"user.name\":\"r2_adhd2\"}",
        "R2_ADHD2"
    )]
    // No display field at all — fall back to the login so the row is never a dash.
    [InlineData("{\"actorLogin\":\"somebody\",\"userLogin\":\"somebody\"}", "somebody")]
    // A display field beats a login field even when both are present.
    [InlineData("{\"actorLogin\":\"lowercase\",\"actorDisplay\":\"ProperCase\"}", "ProperCase")]
    // Raid with only fromDisplayName present resolves to it.
    [InlineData("{\"fromDisplayName\":\"Raider\",\"fromLogin\":\"raider\"}", "Raider")]
    public void ResolvesTheActorNameFromThePayload(string data, string expected)
    {
        DashboardController.ResolveActorNameFromData(data).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)] // no payload
    [InlineData("")] // blank payload
    [InlineData("   ")] // whitespace payload
    [InlineData("not json at all")] // malformed — must not throw
    [InlineData("[1,2,3]")] // a JSON array, not an object
    [InlineData("\"just a string\"")] // a JSON string, not an object
    [InlineData("{\"viewers\":3,\"user.id\":\"405277251\"}")] // an object with none of the name fields
    [InlineData("{\"actorDisplay\":\"\",\"actorLogin\":\"   \"}")] // present but blank names
    public void ReturnsNullWhenNoUsableNameIsPresent(string? data)
    {
        DashboardController.ResolveActorNameFromData(data).Should().BeNull();
    }
}
