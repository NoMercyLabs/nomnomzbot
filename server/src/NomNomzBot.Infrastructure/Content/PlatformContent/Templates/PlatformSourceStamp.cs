// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
