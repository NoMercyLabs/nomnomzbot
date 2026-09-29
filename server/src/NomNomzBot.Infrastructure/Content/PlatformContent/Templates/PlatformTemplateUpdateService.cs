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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// <see cref="IPlatformTemplateUpdateService"/> — reads a channel's copies through each kind's own
/// <see cref="IPlatformTemplateInstaller.ListCopiesAsync"/> (the same listing the admin publish counts) and
/// applies an update through <see cref="IPlatformTemplateInstaller.UpdateCopyAsync"/> (the same rewrite the
/// admin publish performs), inside one transaction, so a refused update leaves the copy as it was.
/// </summary>
public sealed class PlatformTemplateUpdateService(
    IApplicationDbContext db,
    IUnitOfWork uow,
    IActionAuthorizationService authorization,
    IEnumerable<IPlatformTemplateInstaller> installers
) : IPlatformTemplateUpdateService
{
    public async Task<Result<IReadOnlyList<PlatformTemplateUpdateDto>>> ListAsync(
        Guid broadcasterId,
        string kind,
        CancellationToken ct = default
    )
    {
        IPlatformTemplateInstaller? installer = FindInstaller(kind);
        if (installer is null)
            return Result.Failure<IReadOnlyList<PlatformTemplateUpdateDto>>(
                $"'{kind}' is not an installable template kind.",
                "VALIDATION_FAILED"
            );

        List<PublishedDefinition> published = await Published(
                db.PlatformContentDefinitions.Where(d => d.Kind == kind)
            )
            .ToListAsync(ct);

        List<PlatformTemplateUpdateDto> updates = [];
        foreach (PublishedDefinition current in published.OrderBy(p => p.Definition.DisplayName))
        {
            IReadOnlyList<PlatformContentCopy> copies = await installer.ListCopiesAsync(
                current.Definition.Id,
                ct
            );
            updates.AddRange(
                copies
                    .Where(c => c.BroadcasterId == broadcasterId && IsBehind(c, current.Version))
                    .Select(c => ToDto(c, current))
            );
        }

        return Result.Success<IReadOnlyList<PlatformTemplateUpdateDto>>(updates);
    }

    public async Task<Result<PlatformTemplateUpdateDto>> ApplyAsync(
        Guid callerUserId,
        Guid broadcasterId,
        Guid definitionId,
        Guid rowId,
        CancellationToken ct = default
    )
    {
        PublishedDefinition? current = await Published(
                db.PlatformContentDefinitions.Where(d => d.Id == definitionId)
            )
            .FirstOrDefaultAsync(ct);
        IPlatformTemplateInstaller? installer = current is null
            ? null
            : FindInstaller(current.Definition.Kind);
        if (current is null || installer is null)
            return Result.Failure<PlatformTemplateUpdateDto>("Template not found.", "NOT_FOUND");

        Result<bool> allowed = await authorization.AuthorizeActionAsync(
            callerUserId,
            broadcasterId,
            installer.WriteActionKey,
            ct
        );
        if (allowed.IsFailure)
            return allowed.WithValue<PlatformTemplateUpdateDto>(null!);
        if (!allowed.Value)
            return Result.Failure<PlatformTemplateUpdateDto>(
                $"Requires {installer.WriteActionKey}.",
                "FORBIDDEN"
            );

        // Matched on provenance AND channel: another channel's copy, or a row this channel wrote itself, is not
        // an installed copy of this definition here.
        PlatformContentCopy? copy = (
            await installer.ListCopiesAsync(definitionId, ct)
        ).FirstOrDefault(c => c.RowId == rowId && c.BroadcasterId == broadcasterId);
        if (copy is null)
            return Result.Failure<PlatformTemplateUpdateDto>(
                "This channel has no installed copy of that template.",
                "NOT_FOUND"
            );
        if (!IsBehind(copy, current.Version))
            return Result.Failure<PlatformTemplateUpdateDto>(
                "This copy is already on the current version.",
                "ALREADY_CURRENT"
            );

        PlatformTemplateCopyUpdate update = new(
            rowId,
            broadcasterId,
            new PlatformTemplateSource(definitionId, current.Version),
            current.PayloadJson
        );
        Result updated = await uow.ExecuteInTransactionAsync(
            token => installer.UpdateCopyAsync(update, token),
            ct,
            shouldCommit: result => result.IsSuccess
        );
        if (updated.IsFailure)
            return updated.WithValue<PlatformTemplateUpdateDto>(null!);

        return Result.Success(
            new PlatformTemplateUpdateDto(
                rowId,
                definitionId,
                current.Definition.Kind,
                current.Definition.DisplayName,
                current.Version,
                current.Version,
                EditedSinceInstall: false
            )
        );
    }

    private static bool IsBehind(PlatformContentCopy copy, int currentVersion) =>
        copy.SourceVersion is not { } installed || installed < currentVersion;

    private static PlatformTemplateUpdateDto ToDto(
        PlatformContentCopy copy,
        PublishedDefinition current
    ) =>
        new(
            copy.RowId,
            current.Definition.Id,
            current.Definition.Kind,
            current.Definition.DisplayName,
            copy.SourceVersion,
            current.Version,
            EditedSinceInstall: copy.SourceHash != copy.LiveHash
        );

    /// <summary>
    /// The live (not retired) definitions among <paramref name="definitions"/>, joined to their current published
    /// version. Callers filter the definitions first, so every predicate stays translatable to SQL.
    /// </summary>
    private IQueryable<PublishedDefinition> Published(
        IQueryable<PlatformContentDefinition> definitions
    ) =>
        definitions
            .Where(d => d.RetiredAt == null && d.CurrentVersionId != null)
            .Join(
                db.PlatformContentVersions,
                d => d.CurrentVersionId!.Value,
                v => v.Id,
                (d, v) => new PublishedDefinition(d, v.Version, v.PayloadJson)
            );

    private IPlatformTemplateInstaller? FindInstaller(string kind) =>
        installers.FirstOrDefault(i => i.Kind == kind);

    private sealed record PublishedDefinition(
        PlatformContentDefinition Definition,
        int Version,
        string PayloadJson
    );
}
