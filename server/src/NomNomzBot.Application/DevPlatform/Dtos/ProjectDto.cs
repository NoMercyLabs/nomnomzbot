// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.DevPlatform.Projects;

namespace NomNomzBot.Application.DevPlatform.Dtos;

/// <summary>
/// The multi-file project the editor loads and saves (dev-platform.md §4.2, §8): a widget or script is a
/// <see cref="Files"/> set (<c>path → content</c>) plus its <see cref="Manifest"/>. This is the wire shape the
/// <c>GET/PUT .../project</c> endpoints round-trip — identical whether the artifact is one file or a full
/// <c>src/</c> tree. On <c>PUT</c> the server re-builds from these before persisting a version (never trusting a
/// client bundle), so the file set + manifest ARE the source of truth.
/// </summary>
public sealed record ProjectDto(Dictionary<string, string> Files, ProjectManifestDto Manifest);

/// <summary>
/// The project manifest on the wire (dev-platform.md §4.2) — <c>{ entry, kind, framework, dependencies[] }</c>.
/// A one-to-one DTO for <see cref="ProjectManifest"/>; kept distinct from the domain record so the Application
/// contract and the stored manifest can evolve independently.
/// </summary>
public sealed record ProjectManifestDto(
    string Entry,
    string Kind,
    string Framework,
    IReadOnlyList<string>? Dependencies
)
{
    /// <summary>Project the wire manifest onto the domain record the build consumes (null dependencies → empty).</summary>
    public ProjectManifest ToManifest() => new(Entry, Kind, Framework, Dependencies ?? []);

    /// <summary>Lift a stored/domain manifest to its wire DTO.</summary>
    public static ProjectManifestDto FromManifest(ProjectManifest manifest) =>
        new(manifest.Entry, manifest.Kind, manifest.Framework, manifest.Dependencies);
}
