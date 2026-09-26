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
/// <see cref="IPlatformTemplateCatalogService"/> — lists the published, non-retired templates of one kind and
/// installs a copy of a template's CURRENT published version into one channel. The kind's
/// <see cref="IPlatformTemplateInstaller"/> does the copy inside one transaction, so a failed install leaves
/// the channel exactly as it was.
/// </summary>
public sealed class PlatformTemplateCatalogService(
    IApplicationDbContext db,
    IUnitOfWork uow,
    IActionAuthorizationService authorization,
    IEnumerable<IPlatformTemplateInstaller> installers
) : IPlatformTemplateCatalogService
{
    public async Task<Result<PagedList<PlatformTemplateDto>>> ListAsync(
        string kind,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        if (FindInstaller(kind) is null)
            return Result.Failure<PagedList<PlatformTemplateDto>>(
                $"'{kind}' is not an installable template kind.",
                "VALIDATION_FAILED"
            );

        IQueryable<PlatformContentDefinition> published = db.PlatformContentDefinitions.Where(d =>
            d.Kind == kind && d.RetiredAt == null && d.CurrentVersionId != null
        );

        int total = await published.CountAsync(ct);
        List<PlatformTemplateDto> pageRows = await published
            .OrderBy(d => d.DisplayName)
            .ThenBy(d => d.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(
                db.PlatformContentVersions,
                d => d.CurrentVersionId!.Value,
                v => v.Id,
                (d, v) =>
                    new PlatformTemplateDto(
                        d.Id,
                        d.Kind,
                        d.Key,
                        d.DisplayName,
                        d.Description,
                        v.Version,
                        v.PayloadJson
                    )
            )
            .ToListAsync(ct);

        // The join may reorder the paged rows; restore the catalogue order within the page.
        List<PlatformTemplateDto> items =
        [
            .. pageRows.OrderBy(t => t.DisplayName).ThenBy(t => t.Key),
        ];
        return Result.Success(new PagedList<PlatformTemplateDto>(items, page, pageSize, total));
    }

    public async Task<Result<InstalledPlatformTemplateDto>> InstallAsync(
        Guid callerUserId,
        Guid broadcasterId,
        Guid definitionId,
        InstallPlatformTemplateRequest request,
        CancellationToken ct = default
    )
    {
        PlatformContentDefinition? definition =
            await db.PlatformContentDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        IPlatformTemplateInstaller? installer = definition is null
            ? null
            : FindInstaller(definition.Kind);
        if (
            definition is null
            || installer is null
            || definition.RetiredAt is not null
            || definition.CurrentVersionId is not { } currentVersionId
        )
            return Result.Failure<InstalledPlatformTemplateDto>("Template not found.", "NOT_FOUND");

        Result<bool> allowed = await authorization.AuthorizeActionAsync(
            callerUserId,
            broadcasterId,
            installer.WriteActionKey,
            ct
        );
        if (allowed.IsFailure)
            return allowed.WithValue<InstalledPlatformTemplateDto>(null!);
        if (!allowed.Value)
            return Result.Failure<InstalledPlatformTemplateDto>(
                $"Requires {installer.WriteActionKey}.",
                "FORBIDDEN"
            );

        PlatformContentVersion version = await db.PlatformContentVersions.FirstAsync(
            v => v.Id == currentVersionId,
            ct
        );

        PlatformTemplateInstall install = new(
            broadcasterId,
            new PlatformTemplateSource(definition.Id, version.Version),
            version.PayloadJson,
            request.PipelineId
        );

        return await uow.ExecuteInTransactionAsync(
            token => installer.InstallAsync(install, token),
            ct,
            shouldCommit: result => result.IsSuccess
        );
    }

    private IPlatformTemplateInstaller? FindInstaller(string kind) =>
        installers.FirstOrDefault(i => i.Kind == kind);
}
