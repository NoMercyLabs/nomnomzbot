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
