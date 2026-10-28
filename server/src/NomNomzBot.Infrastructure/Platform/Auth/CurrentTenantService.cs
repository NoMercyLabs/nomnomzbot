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
