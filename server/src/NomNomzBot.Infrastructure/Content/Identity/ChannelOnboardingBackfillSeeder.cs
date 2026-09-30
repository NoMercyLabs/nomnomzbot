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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Repairs channels left behind before every owner sign-in went through <see cref="ChannelOnboardingWriter"/>:
/// <list type="number">
/// <item>A moderator-mode tenant whose owner DID sign in later stayed un-onboarded, so the bot never served
/// it. It is promoted only when the owner's own Twitch grant is vaulted on that tenant — a live
/// <c>twitch</c> <see cref="Domain.Integrations.Entities.IntegrationConnection"/> whose account is the
/// broadcaster. Only the owner's sign-in writes that row, so a tenant only a moderator ever opened stays
/// un-onboarded (opt-in).</item>
/// <item>An onboarded channel created before the base <see cref="PlatformConnection"/> existed gets one.</item>
/// </list>
/// Idempotent: a promoted channel is onboarded and a backfilled connection exists, so a re-run selects nothing.
/// The seed handlers of a promoted channel run from <c>OnboardedChannelSeedBackfillService</c> at startup.
/// </summary>
public sealed class ChannelOnboardingBackfillSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChannelOnboardingBackfillSeeder> _logger;

    public ChannelOnboardingBackfillSeeder(
        IApplicationDbContext db,
        TimeProvider clock,
        ILogger<ChannelOnboardingBackfillSeeder> logger
    )
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public int Order => 930;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Channel> promoted = await PromoteOwnerSignedInTenantsAsync(ct);
        int backfilled = await BackfillMissingPlatformConnectionsAsync(ct);

        foreach (Channel channel in promoted)
            _logger.LogWarning(
                "Onboarded channel {ChannelId} ({Name}): its owner had signed in but the tenant was left un-onboarded",
                channel.Id,
                channel.Name
            );

        if (backfilled > 0)
            _logger.LogInformation(
                "Backfilled the base platform connection for {Count} onboarded channel(s)",
                backfilled
            );
    }

    private async Task<List<Channel>> PromoteOwnerSignedInTenantsAsync(CancellationToken ct)
    {
        List<Channel> candidates = await _db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && !c.IsOnboarded && c.TwitchChannelId != null)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return [];

        List<Guid?> candidateIds = [.. candidates.Select(c => (Guid?)c.Id)];
        List<(Guid? BroadcasterId, string? AccountId)> ownerGrants = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(i =>
                candidateIds.Contains(i.BroadcasterId)
                && i.Provider == AuthEnums.IntegrationProvider.Twitch
                && i.DeletedAt == null
            )
            .Select(i => new ValueTuple<Guid?, string?>(i.BroadcasterId, i.ProviderAccountId))
            .ToListAsync(ct);
        HashSet<(Guid?, string?)> signedIn = [.. ownerGrants];

        List<Channel> promoted =
        [
            .. candidates.Where(c => signedIn.Contains((c.Id, c.TwitchChannelId))),
        ];
        if (promoted.Count == 0)
            return [];

        Dictionary<Guid, string> ownerNames = await OwnerDisplayNamesAsync(promoted, ct);
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        foreach (Channel channel in promoted)
            await ChannelOnboardingWriter.OnboardAsync(
                _db,
                channel,
                ownerNames.GetValueOrDefault(channel.OwnerUserId, channel.Name),
                now,
                ct
            );

        return promoted;
    }

    private async Task<int> BackfillMissingPlatformConnectionsAsync(CancellationToken ct)
    {
        List<Channel> missing = await _db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.IsOnboarded && c.ExternalChannelId != "")
            .Where(c =>
                !_db.PlatformConnections.Any(p =>
                    p.Provider == c.Provider && p.ExternalChannelId == c.ExternalChannelId
                )
            )
            .ToListAsync(ct);
        if (missing.Count == 0)
            return 0;

        Dictionary<Guid, string> ownerNames = await OwnerDisplayNamesAsync(missing, ct);
        foreach (Channel channel in missing)
            await ChannelOnboardingWriter.EnsurePlatformConnectionAsync(
                _db,
                channel,
                ownerNames.GetValueOrDefault(channel.OwnerUserId, channel.Name),
                ct
            );

        return missing.Count;
    }

    private Task<Dictionary<Guid, string>> OwnerDisplayNamesAsync(
        List<Channel> channels,
        CancellationToken ct
    )
    {
        List<Guid> ownerIds = [.. channels.Select(c => c.OwnerUserId)];
        return _db
            .Users.IgnoreQueryFilters()
            .Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
