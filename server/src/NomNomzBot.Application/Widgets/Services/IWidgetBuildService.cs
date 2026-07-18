// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DevPlatform.Projects;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The widget compile boundary (widgets-overlays.md §3.2, dev-platform.md §4.2). Pure: no DB. Turns a multi-file
/// widget project (its file set + manifest) into ONE browser-ready bundle — esbuild resolving cross-file imports —
/// plus a deterministic content hash (the overlay cache-bust key). A failed compile is a <see cref="Result"/> failure
/// carrying the build output, never a thrown exception.
/// </summary>
public interface IWidgetBuildService
{
    Task<Result<WidgetBuildOutput>> BuildAsync(
        WidgetBuildInput input,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// A widget project to build: its <paramref name="Manifest"/> (entry / framework ∈ <c>vanilla</c> | <c>react</c> |
/// <c>vue</c> | <c>svelte</c> / declared dependencies) and its <paramref name="Files"/> (<c>path → content</c>). The
/// manifest <c>Entry</c> must exist in <paramref name="Files"/>; esbuild bundles from it, resolving relative imports.
/// </summary>
public sealed record WidgetBuildInput(
    ProjectManifest Manifest,
    IReadOnlyDictionary<string, string> Files
)
{
    /// <summary>The single-file convenience: wraps one authored source into a one-file project (dev-platform.md §4.2).</summary>
    public static WidgetBuildInput SingleFile(string framework, string source)
    {
        (Dictionary<string, string> files, ProjectManifest manifest) = ProjectScaffold.SingleFile(
            "widget",
            framework,
            source
        );
        return new WidgetBuildInput(manifest, files);
    }
}

/// <summary><paramref name="ContentHash"/> is sha256 of <paramref name="CompiledBundle"/> (64 hex, lower-case).</summary>
public sealed record WidgetBuildOutput(string CompiledBundle, string ContentHash, string BuildLog);
