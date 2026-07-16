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
using NomNomzBot.Api.Authorization;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Proves the return-path open-redirect boundary: only a same-origin RELATIVE path survives into the OAuth
/// state — every shape that could re-anchor the browser onto another origin (scheme-relative <c>//host</c>,
/// backslash variants, absolute URLs, CR/LF header splitting) or collide with the token fragment is dropped
/// to null, which the callbacks turn into the origin root.
/// </summary>
public sealed class ReturnPathPolicyTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/commands")]
    [InlineData("/settings?tab=identity")]
    [InlineData("/channels/abc/moderation?page=2&pageSize=25")]
    public void A_same_origin_relative_path_passes_through_unchanged(string path)
    {
        ReturnPathPolicy.Normalize(path).Should().Be(path);
    }

    [Theory]
    [InlineData("//evil.com/phish", "scheme-relative URLs re-anchor the host")]
    [InlineData("/\\evil.com", "browsers treat a backslash like a forward slash here")]
    [InlineData("/commands\\..\\x", "backslashes anywhere are dropped, not normalized")]
    [InlineData("https://evil.com/", "absolute URLs are never a return path")]
    [InlineData("nomnomzbot://callback", "custom schemes ride redirect_uri, not return_to")]
    [InlineData("commands", "a relative segment without a leading slash is ambiguous")]
    [InlineData("/a\rSet-Cookie: x=y", "CR is a header-splitting vector")]
    [InlineData("/a\nLocation: //evil", "LF is a header-splitting vector")]
    [InlineData("/page#fragment", "the callback owns the fragment — it carries the access token")]
    public void An_unsafe_path_is_dropped(string path, string because)
    {
        ReturnPathPolicy.Normalize(path).Should().BeNull(because);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_path_is_null(string? path)
    {
        ReturnPathPolicy.Normalize(path).Should().BeNull();
    }

    [Fact]
    public void An_oversized_path_is_dropped()
    {
        string path = "/" + new string('a', 600);
        ReturnPathPolicy.Normalize(path).Should().BeNull("a 600-char path is nobody's real page");
    }
}
