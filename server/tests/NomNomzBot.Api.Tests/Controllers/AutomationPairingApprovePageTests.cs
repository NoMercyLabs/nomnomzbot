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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.AutomationApi.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Stoney, 2026-11-17, on the device approval page the Stream Deck plugin opened: "i am logged in, i can't click
/// that button... so this does not work", then "the ui is not matching the bot theme". The link a device shows now
/// opens the dashboard's own API devices page with the code filled in.
/// </summary>
public sealed class AutomationPairingApprovePageTests
{
    private static AutomationPairingController Controller() =>
        new(
            Substitute.For<IAutomationPairingService>(),
            Substitute.For<ICurrentTenantService>(),
            Substitute.For<ICurrentUserService>()
        );

    [Fact]
    public void The_approval_link_opens_the_dashboard_page_with_the_code_filled_in()
    {
        IActionResult result = Controller().ApproveDevicePage("N5HTW55G");

        result
            .Should()
            .BeOfType<RedirectResult>()
            .Which.Url.Should()
            .Be("/app/settings/api-devices?code=N5HTW55G");
    }

    [Fact]
    public void A_code_is_escaped_so_it_cannot_change_the_address()
    {
        IActionResult result = Controller().ApproveDevicePage("A B&next=//evil.example");

        result
            .Should()
            .BeOfType<RedirectResult>()
            .Which.Url.Should()
            .Be("/app/settings/api-devices?code=A%20B%26next%3D%2F%2Fevil.example");
    }

    [Fact]
    public void A_link_without_a_code_opens_the_page_empty()
    {
        IActionResult result = Controller().ApproveDevicePage(null);

        result
            .Should()
            .BeOfType<RedirectResult>()
            .Which.Url.Should()
            .Be("/app/settings/api-devices");
    }
}
