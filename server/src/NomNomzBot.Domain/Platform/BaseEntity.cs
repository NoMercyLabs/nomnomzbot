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

public abstract class BaseEntity
{
    // Entities do NOT self-stamp time: AuditableEntityInterceptor owns CreatedAt/UpdatedAt
    // stamping via the injected TimeProvider (the single clock, platform-conventions §3.11).
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
