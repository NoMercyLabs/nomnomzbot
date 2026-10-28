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

namespace NomNomzBot.Application.DevPlatform.Projects;

/// <summary>
/// The manifest of a multi-file dev-platform project (dev-platform.md §4.2). A widget, script, or game is a file set
/// plus this manifest: which file is the build <see cref="Entry"/>, what <see cref="Kind"/> and <see cref="Framework"/>
/// it is, and which external <see cref="Dependencies"/> it declares (each allowlist-checked at build time — never npm).
/// Serialized verbatim into the version row's <c>ManifestJson</c> column.
/// </summary>
public sealed record ProjectManifest(
    string Entry,
    string Kind,
    string Framework,
    IReadOnlyList<string> Dependencies
);
