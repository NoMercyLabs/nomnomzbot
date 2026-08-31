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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A revoked/expired/wrong overlay token used to fail SILENTLY: <c>/overlay/ticket</c> answers 401/403 with
/// no body, and the SDK only logged the rejection to the devtools console — a streamer glancing at the OBS
/// preview saw an empty browser source and nothing else (S062). The SDK now renders a small in-page banner
/// the moment a ticket request comes back rejected, so the failure is visible where the streamer is actually
/// looking. The SDK is inline script served as text, so this asserts on the served script itself — that is
/// the only place the behaviour exists.
/// </summary>
public sealed class OverlaySdkRejectedTokenBannerTests
{
    private static string Sdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult result = (ContentResult)controller.Get();
        return result.Content!;
    }

    [Fact]
    public void A_rejected_ticket_request_shows_a_visible_banner_with_a_human_readable_reason()
    {
        string sdk = Sdk();

        sdk.Should()
            .Contain(
                "Widget token invalid or revoked — reconnect from the dashboard",
                "the streamer must see WHY the overlay is broken, not just a blank browser source"
            );

        int fetchStart = sdk.IndexOf("function fetchTicket(", StringComparison.Ordinal);
        fetchStart
            .Should()
            .BeGreaterThan(-1, "the SDK must still fetch a ticket to connect to the hub");
        int connectStart = sdk.IndexOf("function connect(", fetchStart, StringComparison.Ordinal);
        connectStart.Should().BeGreaterThan(fetchStart);

        string fetchBody = sdk[fetchStart..connectStart];
        fetchBody
            .Should()
            .Contain("401", "the fetch path must recognize an unauthorized ticket request");
        fetchBody
            .Should()
            .Contain("403", "the fetch path must recognize a forbidden ticket request");
        fetchBody
            .Should()
            .Contain(
                "rejected",
                "a rejected token must be distinguishable from a transient network/server error"
            );

        int connectEnd = sdk.IndexOf(
            "function openSocket(",
            connectStart,
            StringComparison.Ordinal
        );
        connectEnd.Should().BeGreaterThan(connectStart);

        sdk[connectStart..connectEnd]
            .Should()
            .Contain(
                "showBanner",
                "a rejected ticket must trigger the visible banner, not just a console log"
            );
    }

    [Fact]
    public void A_successful_reconnect_clears_a_previously_shown_banner()
    {
        string sdk = Sdk();

        sdk.Should()
            .Contain(
                "function hideBanner(",
                "the banner must not remain stuck once the token starts working again"
            );

        int connectStart = sdk.IndexOf("function connect(", StringComparison.Ordinal);
        connectStart.Should().BeGreaterThan(-1);
        int connectEnd = sdk.IndexOf(
            "function openSocket(",
            connectStart,
            StringComparison.Ordinal
        );

        sdk[connectStart..connectEnd]
            .Should()
            .Contain(
                "hideBanner",
                "a ticket that succeeds after a prior rejection must clear the banner"
            );
    }
}
