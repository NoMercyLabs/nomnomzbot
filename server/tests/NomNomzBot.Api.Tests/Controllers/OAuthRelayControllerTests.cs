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
