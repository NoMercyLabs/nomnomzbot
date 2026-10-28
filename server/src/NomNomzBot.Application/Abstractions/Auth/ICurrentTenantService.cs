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

namespace NomNomzBot.Application.Abstractions.Auth;

public interface ICurrentTenantService
{
    // The current tenant (channel) id, widened string? -> Guid? (schema §1.1). Null when no tenant
    // is resolved (anonymous / background contexts before SetTenant).
    Guid? BroadcasterId { get; }

    bool HasTenant { get; }

    void SetTenant(Guid broadcasterId);

    // Drops tenant context so a background service can be reused across tenants.
    void Clear();
}
