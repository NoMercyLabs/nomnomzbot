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
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.PlatformContent;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

internal static class PlatformTemplateCopies
{
    /// <summary>
    /// The installed copies of one definition across every tenant. Cross-tenant by design (the publish blast
    /// radius): the ambient tenant filter is bypassed and <c>DeletedAt == null</c> re-applied, so an admin who
    /// happens to own a channel still sees every other channel's copy. Matched on provenance only, never on
    /// name: a row the channel authored itself is not a copy.
    /// </summary>
    public static async Task<IReadOnlyList<PlatformContentCopy>> ListAsync<TRow>(
        DbSet<TRow> rows,
        Guid definitionId,
        Func<TRow, Guid> id,
        Func<TRow, string> liveHash,
        CancellationToken ct
    )
        where TRow : SoftDeletableEntity, ITenantScoped, IPlatformSourced
    {
        List<TRow> installed = await rows.IgnoreQueryFilters()
            .Where(r => r.DeletedAt == null && r.PlatformSourceDefinitionId == definitionId)
            .ToListAsync(ct);
        return
        [
            .. installed.Select(r => new PlatformContentCopy(
                id(r),
                r.BroadcasterId,
                r.PlatformSourceVersion,
                r.PlatformSourceHash,
                liveHash(r)
            )),
        ];
    }
}
