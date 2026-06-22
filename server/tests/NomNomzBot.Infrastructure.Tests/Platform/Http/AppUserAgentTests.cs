// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using FluentAssertions;
using NomNomzBot.Infrastructure.Platform.Http;

namespace NomNomzBot.Infrastructure.Tests.Platform.Http;

/// <summary>
/// Proves the global outbound User-Agent (applied to every factory HttpClient) is a clean, header-valid
/// product token stamped with the build version, with build metadata (e.g. <c>+&lt;gitsha&gt;</c>) trimmed off.
/// </summary>
public sealed class AppUserAgentTests
{
    [Fact]
    public void Value_is_a_header_valid_product_token_with_a_version_and_no_build_metadata()
    {
        string userAgent = AppUserAgent.Value;

        userAgent.Should().StartWith("NomNomzBot/");
        userAgent.Should().NotContain("+"); // build metadata is trimmed so the product token stays clean

        string version = userAgent["NomNomzBot/".Length..];
        version.Should().NotBeNullOrWhiteSpace();

        // The whole point is that it is usable as a User-Agent header — ParseAdd must accept it.
        ProductInfoHeaderValue.TryParse(userAgent, out _).Should().BeTrue();
    }
}
