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

namespace NomNomzBot.Domain.Platform.Entities;

public class DeletionAuditLog
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string RequestType { get; set; } = null!;

    [MaxLength(64)]
    public string SubjectIdHash { get; set; } = null!;

    [MaxLength(20)]
    public string RequestedBy { get; set; } = null!;

    public List<string> TablesAffected { get; set; } = [];

    public int RowsDeleted { get; set; }

    public DateTime CompletedAt { get; set; }

    // Stamped by the writing service via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — entities do not self-stamp time.
    public DateTime CreatedAt { get; set; }
}
