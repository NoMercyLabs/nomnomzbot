// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The curated set of external libraries a widget/script project may depend on (dev-platform.md §4.2, §7). There is
/// NO npm/registry fetch — supply-chain + SaaS isolation forbid it; a project may import only libraries the bot itself
/// vendors/injects (each an esbuild external). A declared dependency outside this set fails the build up-front
/// (deny-by-default); a bare import of an un-allowlisted module fails when esbuild cannot resolve it. The set grows
/// only by owner decision — <c>nnz.*</c> batteries cover most needs, so deps stay rare.
/// </summary>
public interface IWidgetDependencyAllowlist
{
    /// <summary>True if <paramref name="dependency"/> is on the allowlist (case-insensitive on the bare module name).</summary>
    bool IsAllowed(string dependency);

    /// <summary>
    /// The allowlisted module names esbuild must keep <c>--external</c> (host-injected, not bundled). Today just
    /// <c>vue</c> (mapped to <c>window.Vue</c> by the require-shim); the vue build path externalizes it regardless.
    /// </summary>
    IReadOnlyCollection<string> Externals { get; }
}
