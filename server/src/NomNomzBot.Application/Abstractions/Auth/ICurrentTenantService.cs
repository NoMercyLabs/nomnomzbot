// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
