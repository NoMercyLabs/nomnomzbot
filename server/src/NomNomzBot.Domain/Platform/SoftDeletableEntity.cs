// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
