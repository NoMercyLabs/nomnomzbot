// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Events;
using NomNomzBot.Domain.CustomCode.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Custom-code authoring CRUD + versioning + hot-swap (custom-code.md §3.4). Versions are immutable + append-only;
/// validate-on-save compiles synchronously through <see cref="IScriptExecutor.CompileAsync"/> (rejected versions are
/// still persisted for audit). The active version is a pointer on the script (hot-swap, no row mutation).
/// </summary>
public sealed class CodeScriptService(
    IApplicationDbContext db,
    ICurrentTenantService tenant,
    IScriptExecutor executor,
    IEventBus eventBus,
    TimeProvider clock,
    IWidgetDependencyAllowlist dependencyAllowlist
) : ICodeScriptService
{
    public async Task<Result<PagedList<CodeScriptSummaryDto>>> ListAsync(
        PaginationParams paging,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not Guid bid)
            return Result.Failure<PagedList<CodeScriptSummaryDto>>("No tenant.", "NO_TENANT");

        IQueryable<CodeScript> query = db.CodeScripts.Where(s =>
            s.BroadcasterId == bid && s.DeletedAt == null
        );
        int total = await query.CountAsync(cancellationToken);
        List<CodeScript> scripts = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        List<Guid> versionIds =
        [
            .. scripts
                .Where(s => s.CurrentVersionId != null)
                .Select(s => s.CurrentVersionId!.Value),
        ];
        Dictionary<Guid, CodeScriptVersion> current = await db
            .CodeScriptVersions.Where(v => versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        List<CodeScriptSummaryDto> items =
        [
            .. scripts.Select(s =>
            {
                CodeScriptVersion? v =
                    s.CurrentVersionId is Guid id
                    && current.TryGetValue(id, out CodeScriptVersion? cv)
                        ? cv
                        : null;
                return new CodeScriptSummaryDto(
                    s.Id,
                    s.Name,
                    s.Description,
                    s.IsEnabled,
                    v?.Version,
                    v?.ValidationStatus ?? "none",
                    s.LastRuntimeError,
                    s.LastRanAt,
                    s.CreatedAt,
                    s.UpdatedAt
                );
            }),
        ];
        return Result.Success(
            new PagedList<CodeScriptSummaryDto>(items, paging.Page, paging.PageSize, total)
        );
    }

    public async Task<Result<CodeScriptDetailDto>> GetAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<CodeScriptDetailDto>("Script not found.", "NOT_FOUND");
        return Result.Success(await ToDetailAsync(script, cancellationToken));
    }

    public async Task<Result<CodeScriptDetailDto>> CreateAsync(
        CreateCodeScriptRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (tenant.BroadcasterId is not Guid bid)
            return Result.Failure<CodeScriptDetailDto>("No tenant.", "NO_TENANT");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            return Result.Failure<CodeScriptDetailDto>(
                "Name is required (≤100).",
                "VALIDATION_FAILED"
            );

        bool exists = await db.CodeScripts.AnyAsync(
            s => s.BroadcasterId == bid && s.Name == request.Name && s.DeletedAt == null,
            cancellationToken
        );
        if (exists)
            return Result.Failure<CodeScriptDetailDto>(
                "A script with that name exists.",
                "ALREADY_EXISTS"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        CodeScript script = new()
        {
            BroadcasterId = bid,
            Name = request.Name,
            Description = request.Description,
            Language = "typescript",
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CodeScripts.Add(script);

        CodeScriptVersion version = await BuildVersionAsync(
            script,
            1,
            request.SourceCode,
            now,
            cancellationToken
        );
        db.CodeScriptVersions.Add(version);
        if (version.ValidationStatus == "valid")
        {
            version.PublishedAt = now;
            script.CurrentVersionId = version.Id;
        }
        await db.SaveChangesAsync(cancellationToken);
        await EmitValidatedAsync(version, cancellationToken);

        if (version.ValidationStatus != "valid")
            return Result.Failure<CodeScriptDetailDto>(
                "Script failed validation; the rejected version was saved.",
                "VALIDATION_FAILED"
            );
        return Result.Success(await ToDetailAsync(script, cancellationToken));
    }

    public async Task<Result<CodeScriptVersionDto>> CreateVersionAsync(
        Guid codeScriptId,
        CreateCodeScriptVersionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<CodeScriptVersionDto>("Script not found.", "NOT_FOUND");

        int nextVersion =
            await db
                .CodeScriptVersions.Where(v => v.CodeScriptId == script.Id)
                .MaxAsync(v => (int?)v.Version, cancellationToken)
            ?? 0;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        CodeScriptVersion version = await BuildVersionAsync(
            script,
            nextVersion + 1,
            request.SourceCode,
            now,
            cancellationToken
        );
        db.CodeScriptVersions.Add(version);

        if (version.ValidationStatus == "valid" && request.Publish)
        {
            version.PublishedAt = now;
            script.CurrentVersionId = version.Id;
            script.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        await EmitValidatedAsync(version, cancellationToken);

        return Result.Success(ToVersionDto(version));
    }

    public async Task<Result<CodeScriptDetailDto>> PublishVersionAsync(
        Guid codeScriptId,
        Guid codeScriptVersionId,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<CodeScriptDetailDto>("Script not found.", "NOT_FOUND");
        CodeScriptVersion? version = await db.CodeScriptVersions.FirstOrDefaultAsync(
            v => v.Id == codeScriptVersionId && v.CodeScriptId == script.Id,
            cancellationToken
        );
        if (version is null)
            return Result.Failure<CodeScriptDetailDto>("Version not found.", "NOT_FOUND");
        if (version.ValidationStatus != "valid")
            return Result.Failure<CodeScriptDetailDto>(
                "Version is not valid.",
                "VALIDATION_FAILED"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        Guid? previous = script.CurrentVersionId;
        script.CurrentVersionId = version.Id;
        script.UpdatedAt = now;
        version.PublishedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new CodeScriptVersionPublishedEvent
            {
                BroadcasterId = script.BroadcasterId,
                CodeScriptId = script.Id,
                CodeScriptVersionId = version.Id,
                Version = version.Version,
                PreviousVersionId = previous,
            },
            cancellationToken
        );
        return Result.Success(await ToDetailAsync(script, cancellationToken));
    }

    public async Task<Result<PagedList<CodeScriptVersionDto>>> ListVersionsAsync(
        Guid codeScriptId,
        PaginationParams paging,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<PagedList<CodeScriptVersionDto>>(
                "Script not found.",
                "NOT_FOUND"
            );

        IQueryable<CodeScriptVersion> query = db.CodeScriptVersions.Where(v =>
            v.CodeScriptId == script.Id
        );
        int total = await query.CountAsync(cancellationToken);
        List<CodeScriptVersion> versions = await query
            .OrderByDescending(v => v.Version)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<CodeScriptVersionDto>(
                [.. versions.Select(ToVersionDto)],
                paging.Page,
                paging.PageSize,
                total
            )
        );
    }

    public async Task<Result<ProjectDto>> GetProjectAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<ProjectDto>("Script not found.", "NOT_FOUND");

        // The editor opens the live (current) version, falling back to the newest saved version for a script that was
        // created but never published a valid version.
        CodeScriptVersion? version = script.CurrentVersionId is Guid currentId
            ? await db.CodeScriptVersions.FirstOrDefaultAsync(
                v => v.Id == currentId,
                cancellationToken
            )
            : await db
                .CodeScriptVersions.Where(v => v.CodeScriptId == script.Id)
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(cancellationToken);
        if (version is null)
            return Result.Failure<ProjectDto>("This script has no saved project yet.", "NOT_FOUND");

        return Result.Success(ToProjectDto(version));
    }

    public async Task<Result<CodeScriptVersionDto>> SaveProjectAsync(
        Guid codeScriptId,
        ProjectDto project,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure<CodeScriptVersionDto>("Script not found.", "NOT_FOUND");

        ProjectManifest manifest = project.Manifest.ToManifest();

        // Pre-build gate (dev-platform.md §4.2), mirroring the widget build's guards: entry present, safe paths,
        // allowlisted dependencies. A failure persists nothing.
        Result validation = ProjectValidation.Validate(
            project.Files,
            manifest,
            dependencyAllowlist
        );
        if (validation.IsFailure)
            // Surface the specific reason (missing entry / unsafe path / un-allowlisted dependency) but under one
            // stable, 400-mapped code so the editor treats every save rejection uniformly.
            return Result.Failure<CodeScriptVersionDto>(
                validation.ErrorMessage,
                "VALIDATION_FAILED"
            );

        int nextVersion =
            await db
                .CodeScriptVersions.Where(v => v.CodeScriptId == script.Id)
                .MaxAsync(v => (int?)v.Version, cancellationToken)
            ?? 0;
        DateTime now = clock.GetUtcNow().UtcDateTime;

        // Compile the manifest entry (validate-on-save). Unlike the audit-keeping create/version paths, a project
        // save that fails to compile persists NO version — the caller gets the reason to fix and resubmit.
        CodeScriptVersion version = await BuildVersionFromProjectAsync(
            script,
            nextVersion + 1,
            project.Files,
            manifest,
            now,
            cancellationToken
        );
        if (version.ValidationStatus != "valid")
            return Result.Failure<CodeScriptVersionDto>(
                FirstValidationError(version),
                "VALIDATION_FAILED"
            );

        // A clean compile: append the version, store the whole project, and hot-swap it live (publish).
        db.CodeScriptVersions.Add(version);
        version.PublishedAt = now;
        script.CurrentVersionId = version.Id;
        script.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await EmitValidatedAsync(version, cancellationToken);

        return Result.Success(ToVersionDto(version));
    }

    public async Task<Result> SetEnabledAsync(
        Guid codeScriptId,
        bool isEnabled,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure("Script not found.", "NOT_FOUND");
        script.IsEnabled = isEnabled;
        script.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    )
    {
        CodeScript? script = await LoadAsync(codeScriptId, cancellationToken);
        if (script is null)
            return Result.Failure("Script not found.", "NOT_FOUND");
        db.CodeScripts.Remove(script); // soft-delete via interceptor; versions remain as append-only audit
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<CodeScript?> LoadAsync(Guid id, CancellationToken ct) =>
        tenant.BroadcasterId is Guid bid
            ? await db.CodeScripts.FirstOrDefaultAsync(
                s => s.Id == id && s.BroadcasterId == bid && s.DeletedAt == null,
                ct
            )
            : null;

    private Task<CodeScriptVersion> BuildVersionAsync(
        CodeScript script,
        int versionNumber,
        string sourceCode,
        DateTime now,
        CancellationToken ct
    )
    {
        // Single-file authoring is a one-file project (dev-platform.md §4.2) — an `index.ts` FilesJson entry whose
        // content IS the authored source — built through the shared project core so both authoring paths agree.
        (Dictionary<string, string> files, ProjectManifest manifest) = ProjectScaffold.SingleFile(
            "script",
            "typescript",
            sourceCode
        );
        return BuildVersionFromProjectAsync(script, versionNumber, files, manifest, now, ct);
    }

    // Compiles a script version from a multi-file project: the manifest entry's content is what the executor
    // validate-on-saves, while the WHOLE file set + manifest are stored so a later editor round-trips them. The caller
    // guarantees the entry exists (ProjectValidation). Compiled output + serving contract are unchanged from the
    // single-file path — this is the one place a CodeScriptVersion's compiled/validation fields are populated.
    private async Task<CodeScriptVersion> BuildVersionFromProjectAsync(
        CodeScript script,
        int versionNumber,
        IReadOnlyDictionary<string, string> files,
        ProjectManifest manifest,
        DateTime now,
        CancellationToken ct
    )
    {
        string entryContent = files[manifest.Entry];
        Result<ScriptCompilation> compiled = await executor.CompileAsync(entryContent, ct);

        CodeScriptVersion version = new()
        {
            CodeScriptId = script.Id,
            BroadcasterId = script.BroadcasterId,
            Version = versionNumber,
            SourceCode = entryContent,
            FilesJson = ProjectJson.SerializeFiles(files),
            ManifestJson = ProjectJson.SerializeManifest(manifest),
            CreatedAt = now,
        };
        if (compiled.IsSuccess)
        {
            version.ValidationStatus = "valid";
            version.CompiledJs = compiled.Value.CompiledJs;
            version.CompiledHash = compiled.Value.CompiledHash;
            version.DeclaredCapabilitiesJson = JsonConvert.SerializeObject(
                compiled.Value.DeclaredCapabilities
            );
        }
        else
        {
            version.ValidationStatus = "rejected";
            version.ValidationErrorsJson = JsonConvert.SerializeObject(
                new[]
                {
                    new ScriptValidationError(
                        "syntax",
                        compiled.ErrorMessage ?? "Invalid.",
                        null,
                        null
                    ),
                }
            );
        }
        return version;
    }

    private Task EmitValidatedAsync(CodeScriptVersion version, CancellationToken ct) =>
        eventBus.PublishAsync(
            new CodeScriptValidatedEvent
            {
                BroadcasterId = version.BroadcasterId,
                CodeScriptId = version.CodeScriptId,
                CodeScriptVersionId = version.Id,
                Version = version.Version,
                ValidationStatus = version.ValidationStatus,
                DeclaredCapabilities =
                    Deserialize<List<string>>(version.DeclaredCapabilitiesJson) ?? [],
                Errors =
                    Deserialize<List<ScriptValidationError>>(version.ValidationErrorsJson) ?? [],
            },
            ct
        );

    private async Task<CodeScriptDetailDto> ToDetailAsync(CodeScript script, CancellationToken ct)
    {
        CodeScriptVersion? current = script.CurrentVersionId is Guid id
            ? await db.CodeScriptVersions.FirstOrDefaultAsync(v => v.Id == id, ct)
            : null;
        return new CodeScriptDetailDto(
            script.Id,
            script.Name,
            script.Description,
            script.IsEnabled,
            script.Language,
            script.CurrentVersionId,
            current is null ? null : ToVersionDto(current),
            script.CreatedAt,
            script.UpdatedAt
        );
    }

    // Project a stored version's file set + manifest to the editor's wire shape. A legacy version without a stored
    // project is projected as its one-file scaffold from the compiled entry, so GET always returns a coherent project.
    private static ProjectDto ToProjectDto(CodeScriptVersion version)
    {
        Dictionary<string, string>? files = ProjectJson.DeserializeFiles(version.FilesJson);
        ProjectManifest? manifest = ProjectJson.DeserializeManifest(version.ManifestJson);
        if (files is null || manifest is null)
            (files, manifest) = ProjectScaffold.SingleFile(
                "script",
                "typescript",
                version.SourceCode
            );

        return new ProjectDto(files, ProjectManifestDto.FromManifest(manifest));
    }

    // The reason a just-compiled (not-yet-persisted) version was rejected — the first validation error's message.
    private static string FirstValidationError(CodeScriptVersion version) =>
        Deserialize<List<ScriptValidationError>>(version.ValidationErrorsJson)
            ?.FirstOrDefault()
            ?.Message
        ?? "The script failed validation.";

    private static CodeScriptVersionDto ToVersionDto(CodeScriptVersion v) =>
        new(
            v.Id,
            v.CodeScriptId,
            v.Version,
            v.SourceCode,
            v.CompiledHash ?? string.Empty,
            v.ValidationStatus,
            Deserialize<List<ScriptValidationError>>(v.ValidationErrorsJson) ?? [],
            Deserialize<List<string>>(v.DeclaredCapabilitiesJson) ?? [],
            v.PublishedAt,
            v.CreatedAt
        );

    private static T? Deserialize<T>(string? json)
        where T : class =>
        string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<T>(json);
}
