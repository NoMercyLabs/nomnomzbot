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
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Structural guard for the exact drift that let <c>moderator:manage:shoutouts</c> go missing from the
/// on-demand scope catalogue for weeks: every scope a Helix sub-client actually enforces at runtime
/// (<c>[RequiresTwitchScope]</c>, reflected by the real <see cref="TwitchScopeRegistry"/> — not a mock) must
/// be reachable through <see cref="TwitchScopeRegistry.FullCatalogue"/> — the set the missing-scope banner
/// and its additive re-grant check against. Progressive scopes (CLAUDE.md) means this is deliberately no
/// longer "requested at login" (that would be the 79-scope, 2301-char authorize URL that made Twitch 502);
/// it must instead be structurally impossible for the catalogue itself to silently drop a scope a Helix call
/// depends on, so the on-demand path can never fail to offer it.
/// </summary>
public sealed class TwitchScopeRegistryCoverageTests
{
    [Fact]
    public void FullCatalogue_ContainsEveryScopeDeclaredViaRequiresTwitchScope()
    {
        TwitchScopeRegistry registry = new();

        registry.AllDeclaredScopes.Should().NotBeEmpty();
        foreach (string scope in registry.AllDeclaredScopes)
            registry
                .FullCatalogue.Should()
                .Contain(
                    scope,
                    $"'{scope}' is enforced by a [RequiresTwitchScope]-decorated Helix sub-client method "
                        + "and must stay reachable through the on-demand catalogue or that call will 403 "
                        + "with a permission the streamer was never offered a way to grant"
                );
    }
}
