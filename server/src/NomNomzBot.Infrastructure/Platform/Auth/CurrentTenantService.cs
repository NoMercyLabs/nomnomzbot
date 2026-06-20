// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// ICurrentTenantService implementation. Scoped service that stores the current
/// BroadcasterId for the request/scope. Set by middleware or manually in background services.
/// </summary>
public sealed class CurrentTenantService : ICurrentTenantService
{
    public Guid? BroadcasterId { get; private set; }

    public bool HasTenant => BroadcasterId.HasValue;

    public void SetTenant(Guid broadcasterId)
    {
        BroadcasterId = broadcasterId;
    }

    public void Clear()
    {
        BroadcasterId = null;
    }
}
