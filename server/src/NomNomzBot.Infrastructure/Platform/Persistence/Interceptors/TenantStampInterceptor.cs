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
