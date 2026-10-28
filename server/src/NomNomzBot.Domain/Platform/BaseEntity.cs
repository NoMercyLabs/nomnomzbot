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

public abstract class BaseEntity
{
    // Entities do NOT self-stamp time: AuditableEntityInterceptor owns CreatedAt/UpdatedAt
    // stamping via the injected TimeProvider (the single clock, platform-conventions §3.11).
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
