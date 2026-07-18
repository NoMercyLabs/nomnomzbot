// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
