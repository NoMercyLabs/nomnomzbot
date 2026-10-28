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
