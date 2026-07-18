// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace NomNomzBot.Application.DevPlatform.Projects;

/// <summary>
/// The ONE serializer for a project's stored JSON (dev-platform.md §4.2). The manifest wire shape is camelCase
/// (<c>{ entry, kind, framework, dependencies }</c>) — the spec's <c>nnz.manifest.json</c> shape and what the editor
/// files/manifest API consumes — so both the compile/publish flow AND the <c>SourceCode → FilesJson+ManifestJson</c>
/// backfill migration write the same shape (the migration's lowercase JSON mirrors this exactly). Files are a plain
/// <c>path → content</c> map whose keys (file paths) pass through verbatim.
/// </summary>
public static class ProjectJson
{
    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string SerializeManifest(ProjectManifest manifest) =>
        JsonSerializer.Serialize(manifest, ManifestOptions);

    public static string SerializeFiles(IReadOnlyDictionary<string, string> files) =>
        JsonSerializer.Serialize(files);

    /// <summary>
    /// Read a stored manifest back into its domain record (the inverse of <see cref="SerializeManifest"/>). Returns
    /// <c>null</c> for a legacy row whose <c>ManifestJson</c> is empty/absent, so a caller can fall back to the
    /// single-file scaffold. The camelCase contract matches the write side exactly, so a round-trip is lossless.
    /// </summary>
    public static ProjectManifest? DeserializeManifest(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ProjectManifest>(json, ManifestOptions);

    /// <summary>
    /// Read a stored <c>path → content</c> file set back into a dictionary (the inverse of
    /// <see cref="SerializeFiles"/>). Returns <c>null</c> for a legacy row whose <c>FilesJson</c> is empty/absent.
    /// </summary>
    public static Dictionary<string, string>? DeserializeFiles(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json);
}
