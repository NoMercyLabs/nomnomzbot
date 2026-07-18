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
using NomNomzBot.Application.DevPlatform.Dtos;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Custom-code authoring CRUD + versioning + hot-swap (custom-code.md §3.4). Tenant from ICurrentTenantService;
/// callers pass no BroadcasterId. Versions are immutable + append-only; validate-on-save compiles synchronously.
/// </summary>
public interface ICodeScriptService
{
    Task<Result<PagedList<CodeScriptSummaryDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default
    );

    Task<Result<CodeScriptDetailDto>> GetAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Create a script + Version 1 (validate-on-save). ALREADY_EXISTS on duplicate name; rejected → VALIDATION_FAILED.</summary>
    Task<Result<CodeScriptDetailDto>> CreateAsync(
        CreateCodeScriptRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Append a new immutable version (validate-on-save); publishes on valid only if requested.</summary>
    Task<Result<CodeScriptVersionDto>> CreateVersionAsync(
        Guid codeScriptId,
        CreateCodeScriptVersionRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Hot-swap CurrentVersionId to an existing VALID version. VALIDATION_FAILED if the target isn't valid.</summary>
    Task<Result<CodeScriptDetailDto>> PublishVersionAsync(
        Guid codeScriptId,
        Guid codeScriptVersionId,
        CancellationToken cancellationToken = default
    );

    Task<Result<PagedList<CodeScriptVersionDto>>> ListVersionsAsync(
        Guid codeScriptId,
        PaginationParams paging,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Load a script's current multi-file project (its current version's file set + manifest) for the editor
    /// (dev-platform.md §8). A legacy version without a stored project is projected as its one-file scaffold.
    /// </summary>
    Task<Result<ProjectDto>> GetProjectAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Save a multi-file project (dev-platform.md §8): validate the file set + manifest (entry present, safe paths,
    /// allowlisted dependencies), compile the manifest entry (validate-on-save), and — only on a valid compile —
    /// append a new version, store the whole project, and hot-swap it live. A validation or compile failure returns
    /// the reason and persists NO version.
    /// </summary>
    Task<Result<CodeScriptVersionDto>> SaveProjectAsync(
        Guid codeScriptId,
        ProjectDto project,
        CancellationToken cancellationToken = default
    );

    Task<Result> SetEnabledAsync(
        Guid codeScriptId,
        bool isEnabled,
        CancellationToken cancellationToken = default
    );

    Task<Result> DeleteAsync(Guid codeScriptId, CancellationToken cancellationToken = default);
}
