// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// An opt-in local-IPC dev-mode key (schema A.5). Off by default and never honored for remote callers — it
/// only gates local inter-process control during development. GLOBAL: not tenant-scoped. Only the SHA-256
/// hash of the key is stored.
/// </summary>
public class IpcDevModeKey : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [MaxLength(64)]
    public string KeyHash { get; set; } = null!;

    [MaxLength(100)]
    public string? Label { get; set; }

    public bool IsEnabled { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User? CreatedByUser { get; set; }
}
