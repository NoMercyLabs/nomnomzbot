// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.PlatformContent;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

internal static class PlatformSourceStamp
{
    /// <summary>Records which published template version the row was copied from, and its hash at install.</summary>
    public static void Stamp(
        this IPlatformSourced row,
        PlatformTemplateSource source,
        string contentHash,
        DateTime now
    )
    {
        row.PlatformSourceDefinitionId = source.DefinitionId;
        row.PlatformSourceVersion = source.Version;
        row.PlatformSourceHash = contentHash;
        row.PlatformSourceSyncedAt = now;
    }
}
