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
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Counts the real blast radius of deleting a channel: every tenant-scoped table, grouped into the curated
/// categories of <see cref="ChannelBlastRadiusSources"/>, plus the consequences outside our database that no
/// row count can express.
/// </summary>
public sealed class ChannelDeletePreviewService : IChannelDeletePreviewService
{
    private const int SampleSize = 3;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<ChannelBlastRadiusSource> _sources;

    public ChannelDeletePreviewService(IApplicationDbContext db, TimeProvider timeProvider)
        : this(db, timeProvider, ChannelBlastRadiusSources.All) { }

    // The source map is injectable so a test can exercise the grouping, the remainder and the tenant filter
    // over a focused relational schema; production always gets the full, completeness-checked map.
    internal ChannelDeletePreviewService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        IReadOnlyList<ChannelBlastRadiusSource> sources
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _sources = sources;
    }

    public async Task<Result<ChannelDeletePreviewDto>> PreviewAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelDeletePreviewDto>(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound<ChannelDeletePreviewDto>(broadcasterId);

        Dictionary<string, int> countsByCategory = [];
        foreach (ChannelBlastRadiusSource source in _sources)
        {
            int count = await source.CountAsync(_db, broadcasterGuid, cancellationToken);
            if (count == 0)
                continue;

            countsByCategory[source.CategoryKey] =
                countsByCategory.GetValueOrDefault(source.CategoryKey) + count;
        }

        List<BlastRadiusCategoryDto> categories =
        [
            .. CategoryOrder
                .Where(countsByCategory.ContainsKey)
                .Select(key => new BlastRadiusCategoryDto(key, countsByCategory[key], [])),
        ];

        // Every counted category is an exhaustive `WHERE tenant = @id` over a real column — there is no
        // template-resolved or run-time-only reference here the way there is for sound clips and widgets. So
        // this total is a total, not a floor, and the dialog must NOT weaken it into a "minimum".
        BlastRadiusDto blastRadius = new(categories, IsMinimum: false);

        IReadOnlyList<ExternalConsequenceDto> external = await CountExternalAsync(
            broadcasterGuid,
            cancellationToken
        );

        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        return Result<ChannelDeletePreviewDto>.Success(
            new ChannelDeletePreviewDto(
                channel.Name,
                blastRadius,
                external,
                ChannelDeletionPolicy.RestoreWindowDays,
                ChannelDeletionPolicy.PermanentAfter(nowUtc)
            )
        );
    }

    // The rendering order of the six named categories plus the remainder. The remainder is always last: it is
    // the honest tail of the total, not a headline.
    private static readonly IReadOnlyList<string> CategoryOrder =
    [
        BlastRadiusCategoryKeys.ChannelChat,
        BlastRadiusCategoryKeys.ChannelViewers,
        BlastRadiusCategoryKeys.ChannelAutomations,
        BlastRadiusCategoryKeys.ChannelIntegrations,
        BlastRadiusCategoryKeys.ChannelOverlays,
        BlastRadiusCategoryKeys.ChannelBilling,
        BlastRadiusCategoryKeys.ChannelOther,
    ];

    /// <summary>
    /// The consequences a row count cannot express — things outside our database that stop working. Each is
    /// counted from the rows that actually back it: a reward that never synced to Twitch has no
    /// <c>TwitchRewardId</c> and does not stop working there, and a disabled webhook was already not firing.
    /// A consequence with a zero count is omitted entirely, so every line listed is a real effect.
    /// </summary>
    private async Task<IReadOnlyList<ExternalConsequenceDto>> CountExternalAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<ExternalConsequenceDto> consequences = [];

        List<string> rewardTitles = await _db
            .Rewards.Where(r => r.BroadcasterId == broadcasterId && r.TwitchRewardId != null)
            .OrderBy(r => r.Title)
            .Select(r => r.Title)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.TwitchRewards, rewardTitles);

        // Deleting the channel revokes its overlay token, so every enabled widget's browser source in OBS
        // stops resolving — the source does not error, it goes blank mid-stream.
        List<string> overlayNames = await _db
            .Widgets.Where(w => w.BroadcasterId == broadcasterId && w.IsEnabled)
            .OrderBy(w => w.Name)
            .Select(w => w.Name)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.OverlaySources, overlayNames);

        List<string> eventTypes = await _db
            .EventSubSubscriptions.Where(s => s.BroadcasterId == broadcasterId && s.Enabled)
            .OrderBy(s => s.EventType)
            .Select(s => s.EventType)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.EventSubSubscriptions, eventTypes);

        List<string> guildNames = await _db
            .DiscordGuildConnections.Where(g =>
                g.BroadcasterId == broadcasterId && g.StreamerEnabled
            )
            .OrderBy(g => g.GuildName)
            .Select(g => g.GuildName ?? g.GuildId)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.DiscordNotifications, guildNames);

        List<string> webhookNames = await _db
            .OutboundWebhookEndpoints.Where(e => e.BroadcasterId == broadcasterId && e.IsEnabled)
            .OrderBy(e => e.Name)
            .Select(e => e.Name)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.OutboundWebhooks, webhookNames);

        List<string> providers = await _db
            .IntegrationConnections.Where(c => c.BroadcasterId == broadcasterId)
            .OrderBy(c => c.Provider)
            .Select(c => c.Provider)
            .ToListAsync(cancellationToken);
        Add(consequences, ExternalConsequenceKeys.OAuthConnections, providers);

        return consequences;
    }

    private static void Add(
        List<ExternalConsequenceDto> consequences,
        string consequenceKey,
        List<string> names
    )
    {
        if (names.Count == 0)
            return;

        consequences.Add(
            new ExternalConsequenceDto(consequenceKey, names.Count, [.. names.Take(SampleSize)])
        );
    }
}
