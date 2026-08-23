// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// <see cref="IBotSelfEchoGuard"/>: singleton so the resolved per-tenant bot identity survives across the
/// scoped DbContext each ingest call runs under (mirrors <c>ChannelRegistry</c>'s scope-factory pattern).
/// Resolution order per tenant: an active <c>ChannelBotAuthorization</c> (a connected per-channel custom
/// bot) wins; otherwise the shared platform bot (if connected) applies to every tenant that has no custom
/// bot; otherwise there is no dedicated bot identity (self-host, bot types as the streamer's own account —
/// only <see cref="BotEmittedLine"/> can catch that case). Cached once per tenant and evicted by
/// <see cref="InvalidateTenant"/> / <see cref="InvalidateAll"/> when a bot connects or disconnects.
/// </summary>
public sealed class BotSelfEchoGuard : IBotSelfEchoGuard, IBotIdentityCacheInvalidation
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<Guid, BotIdentity?> _perTenant = new();

    public BotSelfEchoGuard(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> ShouldSuppressAsync(
        Guid tenantId,
        string provider,
        string senderId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        BotIdentity? identity = await ResolveIdentityAsync(tenantId, cancellationToken);
        if (
            identity is { } botIdentity
            && string.Equals(botIdentity.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(botIdentity.SenderId, senderId, StringComparison.Ordinal)
        )
            return true;

        return BotEmittedLine.IsMarked(message);
    }

    public void InvalidateTenant(Guid tenantId) => _perTenant.TryRemove(tenantId, out _);

    public void InvalidateAll() => _perTenant.Clear();

    private async Task<BotIdentity?> ResolveIdentityAsync(Guid tenantId, CancellationToken ct)
    {
        if (_perTenant.TryGetValue(tenantId, out BotIdentity? cached))
            return cached;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        BotIdentity? resolved = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .Where(a => a.BroadcasterId == tenantId && a.IsActive && a.DeletedAt == null)
            .Join(
                db.BotAccounts.IgnoreQueryFilters(),
                a => a.BotAccountId,
                b => b.Id,
                (a, b) =>
                    new
                    {
                        b.Platform,
                        b.BotUserId,
                        b.IsActive,
                        b.DeletedAt,
                    }
            )
            .Where(x => x.IsActive && x.DeletedAt == null)
            .Select(x => new BotIdentity(x.Platform, x.BotUserId))
            .FirstOrDefaultAsync(ct);

        resolved ??= await db
            .BotAccounts.IgnoreQueryFilters()
            .Where(b =>
                b.IdentityType == AuthEnums.BotIdentityType.Shared
                && b.IsActive
                && b.DeletedAt == null
                && b.ConnectionId != null
            )
            .Select(b => new BotIdentity(b.Platform, b.BotUserId))
            .FirstOrDefaultAsync(ct);

        _perTenant[tenantId] = resolved;
        return resolved;
    }

    private sealed record BotIdentity(string Provider, string SenderId);
}
