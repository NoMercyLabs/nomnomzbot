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

namespace NomNomzBot.Domain.Platform;

public abstract class SoftDeletableEntity : BaseEntity
{
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// The Guid of the acting <c>User</c> who performed the soft delete. Stamped automatically by
    /// <c>SoftDeleteInterceptor</c> whenever <see cref="DeletedAt"/> transitions from null to
    /// non-null — never set by hand. During an impersonated (act-as) session this is the platform
    /// OPERATOR who ran the delete, not the impersonated subject (S089c convention: journal/audit
    /// writes attribute the real actor). Null for rows soft-deleted before this column existed, and
    /// for system/background deletes with no ambient user (e.g. an unattended purge job).
    /// </summary>
    public Guid? DeletedBy { get; set; }
}
