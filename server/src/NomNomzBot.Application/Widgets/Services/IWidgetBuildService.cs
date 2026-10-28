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
/// A widget project to build: its <paramref name="Manifest"/> (entry / framework ∈ <c>vanilla</c> |
/// <c>vue</c> / declared dependencies) and its <paramref name="Files"/> (<c>path → content</c>). The
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
        return new(manifest, files);
    }
}

/// <summary><paramref name="ContentHash"/> is sha256 of <paramref name="CompiledBundle"/> (64 hex, lower-case).</summary>
public sealed record WidgetBuildOutput(string CompiledBundle, string ContentHash, string BuildLog);
