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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// <see cref="IPlatformChannelProvisioner"/> over EF Core. Get-or-create keyed by the unique
/// <c>(Provider, ExternalChannelId)</c> index; a concurrent insert that loses the race is caught and the
/// winning row adopted, so the poller never double-creates a tenant. Queries bypass the global tenant filter
/// (provisioning runs outside any resolved-tenant scope), and the new tenant inherits the owner's primary
/// channel profile so it does not silently default to a different deployment mode / tier.
/// </summary>
public sealed class PlatformChannelProvisioner(IApplicationDbContext db)
    : IPlatformChannelProvisioner
{
    // Channel.Name is a login-style slug capped at 25 (schema A.2); a longer platform title is truncated to fit.
    private const int MaxNameLength = 25;

    public async Task<Guid> GetOrCreateAsync(
        Guid ownerUserId,
        string provider,
        string externalChannelId,
        string displayName,
        CancellationToken cancellationToken = default
    )
    {
        Channel? existing = await FindAsync(provider, externalChannelId, cancellationToken);
        if (existing is not null)
            return existing.Id;

        // Inherit the owner's existing (primary) channel profile — ordered by the time-sortable UUIDv7 Id so the
        // oldest (their Twitch channel) wins — so the new platform tenant runs the same deployment mode + tier.
        ChannelProfile? profile = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.OwnerUserId == ownerUserId && c.DeletedAt == null)
            .OrderBy(c => c.Id)
            .Select(c => new ChannelProfile(c.DeploymentMode, c.BillingTierKey))
            .FirstOrDefaultAsync(cancellationToken);

        string name = Truncate(displayName, MaxNameLength);
        Channel channel = new()
        {
            OwnerUserId = ownerUserId,
            Provider = provider,
            ExternalChannelId = externalChannelId,
            TwitchChannelId = null,
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
            IsOnboarded = true,
            DeploymentMode = profile?.DeploymentMode ?? AuthEnums.DeploymentMode.Saas,
            BillingTierKey = profile?.BillingTierKey ?? "free",
        };
        db.Channels.Add(channel);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return channel.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent create won the unique (Provider, ExternalChannelId) race. Detach our unsaved loser
            // and adopt the winner rather than surface a spurious failure to the poller.
            db.Channels.Remove(channel);
            Channel? winner = await FindAsync(provider, externalChannelId, cancellationToken);
            if (winner is null)
                throw; // not a unique-race after all (no row exists) — re-surface the real failure.
            return winner.Id;
        }
    }

    private async Task<Channel?> FindAsync(
        string provider,
        string externalChannelId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Channels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.Provider == provider
                    && c.ExternalChannelId == externalChannelId
                    && c.DeletedAt == null,
                cancellationToken
            );

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record ChannelProfile(string DeploymentMode, string BillingTierKey);
}
