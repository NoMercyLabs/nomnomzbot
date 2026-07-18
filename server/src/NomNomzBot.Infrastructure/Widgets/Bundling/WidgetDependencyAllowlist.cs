// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Widgets.Bundling;

/// <summary>
/// The default (self-host) dependency allowlist (dev-platform.md §4.2). Deny-by-default: only the modules the bot
/// vendors/injects may be depended on, and each is kept an esbuild external (never bundled from a registry). Seed set
/// = <c>vue</c> (host-injected as <c>window.Vue</c>). Grows only by owner decision; extend <see cref="Allowed"/> to add
/// a vetted library and wire its esbuild resolution (external or vendored).
/// </summary>
public sealed class WidgetDependencyAllowlist : IWidgetDependencyAllowlist
{
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        "vue",
    };

    public bool IsAllowed(string dependency) =>
        !string.IsNullOrWhiteSpace(dependency) && Allowed.Contains(dependency.Trim());

    public IReadOnlyCollection<string> Externals => Allowed;
}
