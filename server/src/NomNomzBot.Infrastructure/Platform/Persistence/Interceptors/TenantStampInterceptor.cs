// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

/// <summary>
/// Stamps the BroadcasterId on new ITenantScoped entities when they are added,
/// using the current tenant from ICurrentTenantService.
/// </summary>
public sealed class TenantStampInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentTenantService _currentTenantService;

    public TenantStampInterceptor(ICurrentTenantService currentTenantService)
    {
        _currentTenantService = currentTenantService;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            StampTenant(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is not null)
        {
            StampTenant(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void StampTenant(DbContext context)
    {
        Guid? broadcasterId = _currentTenantService.BroadcasterId;

        if (broadcasterId is null || broadcasterId.Value == Guid.Empty)
        {
            return;
        }

        foreach (EntityEntry<ITenantScoped> entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State == EntityState.Added && entry.Entity.BroadcasterId == Guid.Empty)
            {
                entry.Entity.BroadcasterId = broadcasterId.Value;
            }
        }
    }
}
