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
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The site's Content-Security-Policy allows scripts from this site only. An inline script on the relay page was
/// blocked, so a connect window never reported back, never closed and never returned to the page it came from.
/// </summary>
public sealed class OAuthRelayControllerTests
{
    [Fact]
    public void Page_LoadsItsScriptFromTheSite_NeverInline()
    {
        ContentResult page = (ContentResult)new OAuthRelayController().Get();

        page.ContentType.Should().Be("text/html");
        page.Content.Should().Contain("<script src=\"/oauth-relay.js\"></script>");
        page.Content.Should()
            .NotContain("<script>", "an inline script is blocked by the site's policy");
    }

    [Fact]
    public void Script_ReportsBackToTheOpenerAndReturnsToTheSamePage()
    {
        ContentResult script = (ContentResult)new OAuthRelayController().GetScript();

        script.ContentType.Should().Be("text/javascript");
        script.Content.Should().Contain("oauth_relay").And.Contain("window.opener");
        script.Content.Should().Contain("window.location.replace(safeBack)");
    }
}
