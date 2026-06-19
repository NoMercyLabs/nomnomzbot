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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
