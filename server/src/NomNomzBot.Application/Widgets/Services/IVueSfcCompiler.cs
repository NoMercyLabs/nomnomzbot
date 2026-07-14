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
/// Compiles a single-file Vue component (<c>&lt;script setup lang="ts"&gt;</c> + template + scoped style) into one
/// browser ES module plus its (scoped) CSS. Pure and stateless from the caller's view — no DB, no filesystem.
/// The returned <see cref="VueSfcOutput.ModuleCode"/> keeps its <c>vue</c> imports bare/external (the host injects
/// the Vue runtime) and still carries TS/JSX syntax (the widget build's esbuild stage strips it). A compile error
/// is a <see cref="Result"/> failure carrying the code-framed message, never a thrown exception.
/// </summary>
public interface IVueSfcCompiler
{
    Result<VueSfcOutput> Compile(string source, string filename);
}

/// <summary>
/// The compiled component. <paramref name="ModuleCode"/> default-exports the component object (bound as
/// <c>__sfc_main__</c>); <paramref name="Css"/> is the collected scoped CSS (empty when the SFC has no styles).
/// </summary>
public sealed record VueSfcOutput(string ModuleCode, string Css);
