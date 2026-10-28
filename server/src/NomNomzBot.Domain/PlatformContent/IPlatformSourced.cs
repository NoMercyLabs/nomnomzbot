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

namespace NomNomzBot.Domain.PlatformContent;

/// <summary>
/// A tenant row that can be installed from a platform template (platform-admin.md §3.3). The pointer is
/// provenance, never a live FK: install copies the template, and these four fields let a later publish find
/// the copies and tell an untouched one from a customized one.
/// </summary>
public interface IPlatformSourced
{
    /// <summary>The <c>PlatformContentDefinition</c> this row was installed from; null when tenant-authored.</summary>
    Guid? PlatformSourceDefinitionId { get; set; }

    /// <summary>The <c>PlatformContentVersion.Version</c> installed.</summary>
    int? PlatformSourceVersion { get; set; }

    /// <summary>Hash of the row's template-shaped fields at install time — "untouched" while it still matches.</summary>
    string? PlatformSourceHash { get; set; }

    /// <summary>When this row last received platform content.</summary>
    DateTime? PlatformSourceSyncedAt { get; set; }
}
