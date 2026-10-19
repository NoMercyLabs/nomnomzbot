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
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the chat color the activity feed hands the Supporters page. A gift or sub recipient is named only in the
/// event payload (<c>recipientUserId</c>), so the color has to come from that user, not from the local row of the
/// event's own <c>UserId</c> (the gifter), or the recipient renders white.
/// </summary>
public sealed class DashboardActivityColorTests
{
    private const string ReceivedGiftPayload =
        "{\"recipientUserId\":\"777\",\"recipientDisplayName\":\"LuckyLuna\",\"gifterUserId\":\"491902010\"}";

    private static readonly User Gifter = new() { TwitchUserId = "491902010", Color = "#111111" };
    private static readonly User Luna = new() { TwitchUserId = "777", Color = "#FF4500" };

    private static readonly Dictionary<string, User> ByTwitchId = new()
    {
        ["491902010"] = Gifter,
        ["777"] = Luna,
    };

    [Fact]
    public void A_gift_recipient_gets_their_own_color_not_the_gifters()
    {
        string? color = DashboardController.ResolveActivityColor(
            ReceivedGiftPayload,
            Gifter,
            ByTwitchId
        );

        color.Should().Be("#FF4500");
    }

    [Fact]
    public void A_recipient_with_no_local_row_has_no_color_rather_than_the_gifters()
    {
        Dictionary<string, User> onlyGifter = new() { ["491902010"] = Gifter };

        string? color = DashboardController.ResolveActivityColor(
            ReceivedGiftPayload,
            Gifter,
            onlyGifter
        );

        color.Should().BeNull();
    }

    [Fact]
    public void An_event_without_a_recipient_keeps_the_actor_color()
    {
        string? color = DashboardController.ResolveActivityColor(
            "{\"userId\":\"491902010\"}",
            Gifter,
            ByTwitchId
        );

        color.Should().Be("#111111");
    }
}
