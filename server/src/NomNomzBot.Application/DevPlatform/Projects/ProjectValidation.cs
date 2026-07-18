// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Application.DevPlatform.Projects;

/// <summary>
/// The kind-neutral pre-build gate for a multi-file project (dev-platform.md §4.2): the manifest entry must be one
/// of the files, every path must be a safe project-relative path (no traversal), and every declared dependency must
/// be allowlisted (deny-by-default, no npm). Mirrors the guards the widget build runs inline
/// (<c>EsbuildWidgetBuildService</c>) so the script path — which compiles a single module and never reaches esbuild —
/// enforces the SAME rules before it persists a version. Pure: no DB, no disk; a failure is a coded
/// <see cref="Result"/>, never a throw.
/// </summary>
public static class ProjectValidation
{
    public static Result Validate(
        IReadOnlyDictionary<string, string> files,
        ProjectManifest manifest,
        IWidgetDependencyAllowlist allowlist
    )
    {
        if (!files.ContainsKey(manifest.Entry))
            return Result.Failure(
                $"The manifest entry '{manifest.Entry}' is not present in the project files.",
                "PROJECT_ENTRY_MISSING"
            );

        // A project's file paths are untrusted input and become real files on disk at build time — reject any path
        // that could escape the project root (rooted, drive-qualified, or containing a `..` segment).
        foreach (string path in files.Keys)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Result.Failure("A project file path is empty.", "PROJECT_PATH_INVALID");

            string normalized = path.Replace('\\', '/');
            if (
                Path.IsPathRooted(normalized)
                || normalized.StartsWith('/')
                || normalized.Contains(':')
                || normalized.Split('/').Any(segment => segment == "..")
            )
                return Result.Failure(
                    $"Project file path '{path}' is not a safe relative path.",
                    "PROJECT_PATH_INVALID"
                );
        }

        // Deny-by-default: a declared dependency outside the allowlist fails up-front (there is no npm install).
        foreach (string dependency in manifest.Dependencies)
            if (!allowlist.IsAllowed(dependency))
                return Result.Failure(
                    $"Dependency '{dependency}' is not on the allowlist — only vetted, bot-provided libraries may be "
                        + $"used (there is no npm install). Allowed: {string.Join(", ", allowlist.Externals)}.",
                    "PROJECT_DEPENDENCY_NOT_ALLOWED"
                );

        return Result.Success();
    }
}
