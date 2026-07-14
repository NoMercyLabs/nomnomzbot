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

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The widget compile boundary (widgets-overlays.md §3.2). Pure: no DB. Turns a widget's authored source into a
/// browser-ready bundle plus a deterministic content hash (the overlay cache-bust key). A failed compile is a
/// <see cref="Result"/> failure carrying the build output, never a thrown exception.
/// </summary>
public interface IWidgetBuildService
{
    Task<Result<WidgetBuildOutput>> BuildAsync(
        WidgetBuildInput input,
        CancellationToken cancellationToken = default
    );
}

/// <summary><paramref name="Framework"/> ∈ <c>vanilla</c> | <c>react</c> | <c>vue</c> | <c>svelte</c>.</summary>
public sealed record WidgetBuildInput(string Framework, string SourceCode);

/// <summary><paramref name="ContentHash"/> is sha256 of <paramref name="CompiledBundle"/> (64 hex, lower-case).</summary>
public sealed record WidgetBuildOutput(string CompiledBundle, string ContentHash, string BuildLog);
