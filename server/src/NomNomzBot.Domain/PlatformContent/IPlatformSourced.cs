// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
