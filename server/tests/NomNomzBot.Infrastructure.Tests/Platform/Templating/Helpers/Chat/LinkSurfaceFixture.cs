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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

/// <summary>A valid Mastodon host and link label for the link namespace.</summary>
internal static class LinkSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key == "link.mastodon" ? [":mastodon.social|Live now"] : [];
}
