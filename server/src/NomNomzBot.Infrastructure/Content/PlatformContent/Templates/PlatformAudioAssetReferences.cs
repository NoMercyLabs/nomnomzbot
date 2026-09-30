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
using NomNomzBot.Domain.PlatformContent.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Which <c>sound_clip</c> templates name which platform audio file. Read from the stored payloads of every
/// version (drafts included) of every non-retired definition, so a file never vanishes under a draft that is
/// about to be published. A retired definition holds nothing.
/// </summary>
internal static class PlatformAudioAssetReferences
{
    /// <summary>Asset id → the display names of the non-retired templates that name it, sorted.</summary>
    public static async Task<Dictionary<Guid, List<string>>> TemplatesByAssetAsync(
        IApplicationDbContext db,
        CancellationToken ct
    )
    {
        List<TemplatePayloadRow> rows = await db
            .PlatformContentVersions.Join(
                db.PlatformContentDefinitions.Where(d =>
                    d.Kind == PlatformContentKinds.SoundClip && d.RetiredAt == null
                ),
                v => v.DefinitionId,
                d => d.Id,
                (v, d) => new TemplatePayloadRow(d.DisplayName, v.PayloadJson)
            )
            .ToListAsync(ct);

        return rows.Select(r =>
                (AssetId: SoundClipTemplatePayload.ReadAssetId(r.PayloadJson), r.DisplayName)
            )
            .Where(r => r.AssetId is not null)
            .GroupBy(r => r.AssetId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.DisplayName).Distinct().Order(StringComparer.Ordinal).ToList()
            );
    }

    /// <summary>The audio files any version of one definition names, whatever its state.</summary>
    public static async Task<HashSet<Guid>> AssetsNamedByAsync(
        IApplicationDbContext db,
        Guid definitionId,
        CancellationToken ct
    )
    {
        List<string> payloads = await db
            .PlatformContentVersions.Where(v => v.DefinitionId == definitionId)
            .Select(v => v.PayloadJson)
            .ToListAsync(ct);
        return
        [
            .. payloads
                .Select(SoundClipTemplatePayload.ReadAssetId)
                .Where(id => id is not null)
                .Select(id => id!.Value),
        ];
    }

    private sealed record TemplatePayloadRow(string DisplayName, string PayloadJson);
}
