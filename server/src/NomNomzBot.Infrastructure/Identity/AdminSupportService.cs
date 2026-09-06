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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The cross-tenant support desk (S-ADMIN-7a). Every read here deliberately crosses the tenant boundary, so
/// every entry point gates and audits FIRST through <see cref="IPlatformIamService.AuthorizePlatformAsync"/>
/// — the same funnel <c>PlatformAdminService.StartImpersonationAsync</c> uses — naming the acting operator
/// and the subject before a single subject row is touched.
/// </summary>
public sealed class AdminSupportService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    IBillingTierService billingTiers,
    IEventJournal eventJournal
) : IAdminSupportService
{
    public async Task<Result<PagedList<SupportPersonSearchResultDto>>> SearchPeopleAsync(
        Guid actingPrincipalId,
        string search,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(search))
            return Result.Failure<PagedList<SupportPersonSearchResultDto>>(
                "A search term is required.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<PagedList<SupportPersonSearchResultDto>>(
                "A justification is required to search people across tenants.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            actingPrincipalId,
            justification,
            $"search:{search}",
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<PagedList<SupportPersonSearchResultDto>>(null!);

        string term = search.Trim().ToLowerInvariant();
        IQueryable<User> matches = db.Users.Where(u =>
            u.UsernameNormalized.Contains(term)
            || u.DisplayName.ToLower().Contains(term)
            || u.TwitchUserId == term
        );

        int total = await matches.CountAsync(ct);
        List<User> page = await matches
            .OrderBy(u => u.UsernameNormalized)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<Guid> userIds = page.Select(u => u.Id).ToList();
        Dictionary<Guid, int> tenantCounts = await CountTenantsAsync(userIds, ct);

        List<SupportPersonSearchResultDto> items = page.Select(
                u => new SupportPersonSearchResultDto(
                    u.Id,
                    u.Username,
                    u.DisplayName,
                    u.Platform,
                    u.TwitchUserId,
                    u.IsPlatformPrincipal,
                    u.IsAnonymized,
                    u.LastSeenAt,
                    tenantCounts.TryGetValue(u.Id, out int count) ? count : 0
                )
            )
            .ToList();

        return Result.Success(
            new PagedList<SupportPersonSearchResultDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<SupportPersonViewDto>> GetPersonAsync(
        Guid actingPrincipalId,
        Guid subjectUserId,
        string justification,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<SupportPersonViewDto>(
                "A justification is required to open a person's cross-tenant record.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            actingPrincipalId,
            justification,
            $"user:{subjectUserId}",
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<SupportPersonViewDto>(null!);

        User? subject = await db.Users.FirstOrDefaultAsync(u => u.Id == subjectUserId, ct);
        if (subject is null)
            return Result.Failure<SupportPersonViewDto>("Unknown user.", "NOT_FOUND");

        List<UserIdentity> identities = await db
            .UserIdentities.IgnoreQueryFilters()
            .Where(i => i.UserId == subjectUserId && i.DeletedAt == null)
            .ToListAsync(ct);

        List<Channel> ownedChannels = await db
            .Channels.Where(c => c.OwnerUserId == subjectUserId)
            .ToListAsync(ct);
        List<Guid> ownedChannelIds = ownedChannels.Select(c => c.Id).ToList();

        List<ChannelCommunityStanding> communityStandings = await db
            .ChannelCommunityStandings.IgnoreQueryFilters()
            .Where(s => s.UserId == subjectUserId)
            .ToListAsync(ct);

        List<UserModerationHistory> moderationHistory = await db
            .UserModerationHistories.IgnoreQueryFilters()
            .Where(h => h.SubjectUserId == subjectUserId)
            .ToListAsync(ct);

        List<UserTrustScore> trustScores = await db
            .UserTrustScores.IgnoreQueryFilters()
            .Where(t => t.SubjectUserId == subjectUserId)
            .ToListAsync(ct);

        List<ChannelModerationStanding> moderationStandings = await LoadModerationStandingsAsync(
            subject,
            identities,
            ct
        );

        List<PlatformConnection> platformConnections =
            ownedChannelIds.Count == 0
                ? []
                : await db
                    .PlatformConnections.Where(p => ownedChannelIds.Contains(p.ChannelId))
                    .ToListAsync(ct);

        List<SupportPersonIamRoleDto> iamRoles = await LoadIamRolesAsync(subjectUserId, ct);

        // One name lookup for every tenant any fact above pointed at, so each row can say WHICH channel it
        // belongs to rather than showing a bare guid.
        HashSet<Guid> referencedTenants =
        [
            .. ownedChannelIds,
            .. communityStandings.Select(s => s.BroadcasterId),
            .. moderationHistory.Select(h => h.BroadcasterId),
            .. trustScores.Select(t => t.BroadcasterId),
            .. moderationStandings.Select(s => s.BroadcasterId),
            .. iamRoles
                .Where(r => r.ScopeBroadcasterId is not null)
                .Select(r => r.ScopeBroadcasterId!.Value),
        ];
        Dictionary<Guid, string> channelNames = await ChannelNamesAsync(referencedTenants, ct);

        List<SupportPersonEntitlementDto> entitlements = await LoadEntitlementsAsync(
            ownedChannels,
            ct
        );

        return Result.Success(
            new SupportPersonViewDto(
                subject.Id,
                subject.Username,
                subject.DisplayName,
                subject.Platform,
                subject.TwitchUserId,
                subject.IsPlatformPrincipal,
                subject.IsAnonymized,
                subject.LastSeenAt,
                identities
                    .Select(i => new SupportPersonIdentityDto(
                        i.Provider,
                        i.ProviderUserId,
                        i.ProviderUsername,
                        i.IsPrimary,
                        i.LinkedAt,
                        i.LastLoginAt
                    ))
                    .ToList(),
                platformConnections
                    .Select(p => new SupportPersonPlatformConnectionDto(
                        p.ChannelId,
                        NameOf(channelNames, p.ChannelId),
                        p.Provider,
                        p.ExternalChannelId,
                        p.DisplayName,
                        p.IsPrimary,
                        p.IsLive
                    ))
                    .ToList(),
                iamRoles
                    .Select(r =>
                        r.ScopeBroadcasterId is null
                            ? r
                            : r with
                            {
                                ScopeChannelName = NameOf(channelNames, r.ScopeBroadcasterId.Value),
                            }
                    )
                    .ToList(),
                communityStandings
                    .Select(s => new SupportPersonCommunityStandingDto(
                        s.BroadcasterId,
                        NameOf(channelNames, s.BroadcasterId),
                        s.Standing.ToString(),
                        s.LevelValue,
                        s.Source.ToString(),
                        s.SubTier,
                        s.LastSeenAt
                    ))
                    .ToList(),
                moderationStandings
                    .Select(s => new SupportPersonModerationStandingDto(
                        s.BroadcasterId,
                        NameOf(channelNames, s.BroadcasterId),
                        s.Provider,
                        s.Standing,
                        s.Reason,
                        s.CreatedAt
                    ))
                    .ToList(),
                moderationHistory
                    .Select(h => new SupportPersonModerationHistoryDto(
                        h.BroadcasterId,
                        NameOf(channelNames, h.BroadcasterId),
                        h.TimeoutCount,
                        h.BanCount,
                        h.WarningCount,
                        h.MessagesDeletedCount,
                        h.LastActionAt,
                        h.LastActionType
                    ))
                    .ToList(),
                trustScores
                    .Select(t => new SupportPersonTrustScoreDto(
                        t.BroadcasterId,
                        NameOf(channelNames, t.BroadcasterId),
                        t.TrustScore,
                        t.HeatScore,
                        t.LastHeatEventAt,
                        t.ComputedAt
                    ))
                    .ToList(),
                entitlements
            )
        );
    }

    public async Task<Result<PagedList<SupportPersonHistoryEntryDto>>> GetPersonHistoryAsync(
        Guid actingPrincipalId,
        Guid subjectUserId,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure<PagedList<SupportPersonHistoryEntryDto>>(
                "A justification is required to replay a person's cross-tenant history.",
                "VALIDATION_FAILED"
            );

        Result authorized = await RequireAsync(
            actingPrincipalId,
            justification,
            $"user:{subjectUserId}:history",
            ct
        );
        if (authorized.IsFailure)
            return authorized.WithValue<PagedList<SupportPersonHistoryEntryDto>>(null!);

        // BroadcasterId left null on purpose — the whole point of this desk is replaying what happened to
        // this person EVERYWHERE, not inside one tenant's own audit UI.
        Result<PagedList<EventRecord>> journal = await eventJournal.QueryAsync(
            new EventJournalQuery(
                BroadcasterId: null,
                EventType: null,
                FromUtc: null,
                ToUtc: null,
                ActorUserId: subjectUserId,
                Page: pagination.Page,
                PageSize: pagination.PageSize
            ),
            ct
        );
        if (journal.IsFailure)
            return journal.WithValue<PagedList<SupportPersonHistoryEntryDto>>(null!);

        HashSet<Guid> referencedTenants = journal
            .Value.Items.Where(e => e.BroadcasterId is not null)
            .Select(e => e.BroadcasterId!.Value)
            .ToHashSet();
        Dictionary<Guid, string> channelNames = await ChannelNamesAsync(referencedTenants, ct);

        List<SupportPersonHistoryEntryDto> items = journal
            .Value.Items.Select(e => new SupportPersonHistoryEntryDto(
                e.EventId,
                e.BroadcasterId,
                e.BroadcasterId is null ? null : NameOf(channelNames, e.BroadcasterId.Value),
                e.EventType,
                e.Source,
                e.OccurredAt
            ))
            .ToList();

        return Result.Success(
            new PagedList<SupportPersonHistoryEntryDto>(
                items,
                journal.Value.Page,
                journal.Value.PageSize,
                journal.Value.TotalCount
            )
        );
    }

    /// <summary>
    /// The bot-side moderation rows for this person. <see cref="ChannelModerationStanding.UserId"/> is a RAW
    /// platform id, so the join runs through the person's PROVEN platform identities (plus the denormalized
    /// <c>User.TwitchUserId</c>) and the provider must match too — one human's Twitch mute is not their Kick
    /// mute.
    /// </summary>
    private async Task<List<ChannelModerationStanding>> LoadModerationStandingsAsync(
        User subject,
        List<UserIdentity> identities,
        CancellationToken ct
    )
    {
        HashSet<(string Provider, string ExternalId)> platformIds = identities
            .Select(i => (i.Provider, i.ProviderUserId))
            .ToHashSet();
        if (!string.IsNullOrWhiteSpace(subject.TwitchUserId))
            platformIds.Add((AuthEnums.Platform.Twitch, subject.TwitchUserId));

        if (platformIds.Count == 0)
            return [];

        List<string> externalIds = platformIds.Select(p => p.ExternalId).Distinct().ToList();
        List<ChannelModerationStanding> candidates = await db
            .ChannelModerationStandings.IgnoreQueryFilters()
            .Where(s => externalIds.Contains(s.UserId))
            .ToListAsync(ct);

        // The provider pairing is re-applied in memory: a two-column IN is not translatable, and matching on
        // the id alone would attribute another platform's user id to this person.
        return candidates.Where(s => platformIds.Contains((s.Provider, s.UserId))).ToList();
    }

    /// <summary>Every LIVE platform-IAM role this person holds through their IAM principal(s).</summary>
    private async Task<List<SupportPersonIamRoleDto>> LoadIamRolesAsync(
        Guid subjectUserId,
        CancellationToken ct
    )
    {
        List<IamPrincipal> principals = await db
            .IamPrincipals.IgnoreQueryFilters()
            .Where(p => p.UserId == subjectUserId && p.DeletedAt == null)
            .ToListAsync(ct);
        if (principals.Count == 0)
            return [];

        List<Guid> principalIds = principals.Select(p => p.Id).ToList();
        List<IamRoleAssignment> assignments = await db
            .IamRoleAssignments.Where(a =>
                principalIds.Contains(a.PrincipalId) && a.RevokedAt == null
            )
            .ToListAsync(ct);
        if (assignments.Count == 0)
            return [];

        List<Guid> roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        Dictionary<Guid, string> roleNames = await db
            .IamRoles.IgnoreQueryFilters()
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        Dictionary<Guid, string> principalNames = principals.ToDictionary(p => p.Id, p => p.Name);

        return assignments
            .Select(a => new SupportPersonIamRoleDto(
                a.PrincipalId,
                principalNames.TryGetValue(a.PrincipalId, out string? principalName)
                    ? principalName
                    : "",
                roleNames.TryGetValue(a.RoleId, out string? roleName) ? roleName : "",
                a.ScopeChannelId,
                null,
                a.ExpiresAt
            ))
            .ToList();
    }

    /// <summary>
    /// The effective entitlement of each channel this person owns — the tier <see cref="IBillingTierService"/>
    /// actually resolves, plus the LIVE comps behind it. A tenant whose entitlement cannot be resolved is
    /// OMITTED rather than reported on a guessed tier.
    /// </summary>
    private async Task<List<SupportPersonEntitlementDto>> LoadEntitlementsAsync(
        List<Channel> ownedChannels,
        CancellationToken ct
    )
    {
        if (ownedChannels.Count == 0)
            return [];

        List<Guid> channelIds = ownedChannels.Select(c => c.Id).ToList();
        List<EntitlementGrant> grants = await db
            .EntitlementGrants.IgnoreQueryFilters()
            .Where(g => channelIds.Contains(g.BroadcasterId) && g.DeletedAt == null)
            .ToListAsync(ct);

        List<SupportPersonEntitlementDto> result = [];
        foreach (Channel channel in ownedChannels)
        {
            Result<EntitlementDto> entitlement = await billingTiers.GetEntitlementAsync(
                channel.Id,
                ct
            );
            if (entitlement.IsFailure)
                continue;

            result.Add(
                new SupportPersonEntitlementDto(
                    channel.Id,
                    channel.Name,
                    entitlement.Value.TierKey,
                    grants
                        .Where(g => g.BroadcasterId == channel.Id)
                        .Select(g => new SupportPersonEntitlementGrantDto(
                            g.Id,
                            g.GrantedTierId,
                            g.Reason,
                            g.IssuedAt,
                            g.ExpiresAt
                        ))
                        .ToList()
                )
            );
        }
        return result;
    }

    /// <summary>How many distinct tenants each of <paramref name="userIds"/> is known in — channels they own
    /// plus every channel that holds a community-standing row for them.</summary>
    private async Task<Dictionary<Guid, int>> CountTenantsAsync(
        List<Guid> userIds,
        CancellationToken ct
    )
    {
        if (userIds.Count == 0)
            return [];

        // Anonymous projections: the type is unnameable, the one case `var` is legal (house style).
        var standingPairs = await db
            .ChannelCommunityStandings.IgnoreQueryFilters()
            .Where(s => userIds.Contains(s.UserId))
            .Select(s => new { s.UserId, s.BroadcasterId })
            .Distinct()
            .ToListAsync(ct);
        var ownedPairs = await db
            .Channels.Where(c => userIds.Contains(c.OwnerUserId))
            .Select(c => new { UserId = c.OwnerUserId, BroadcasterId = c.Id })
            .ToListAsync(ct);

        Dictionary<Guid, HashSet<Guid>> tenantsByUser = [];
        foreach (Guid userId in userIds)
            tenantsByUser[userId] = [];
        foreach (var pair in standingPairs)
            tenantsByUser[pair.UserId].Add(pair.BroadcasterId);
        foreach (var pair in ownedPairs)
            tenantsByUser[pair.UserId].Add(pair.BroadcasterId);

        return tenantsByUser.ToDictionary(entry => entry.Key, entry => entry.Value.Count);
    }

    private async Task<Dictionary<Guid, string>> ChannelNamesAsync(
        HashSet<Guid> tenantIds,
        CancellationToken ct
    )
    {
        if (tenantIds.Count == 0)
            return [];

        List<Guid> ids = tenantIds.ToList();
        return await db
            .Channels.IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    /// <summary>The tenant's display name, or its id when the channel row is gone — never a blank label.</summary>
    private static string NameOf(Dictionary<Guid, string> names, Guid tenantId) =>
        names.TryGetValue(tenantId, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : tenantId.ToString();

    /// <summary>
    /// The one authorization funnel — decides AND audits (allowed or denied) on SaaS, naming the acting
    /// operator and the subject in <c>TargetResource</c>. Called BEFORE any subject row is read.
    /// </summary>
    private async Task<Result> RequireAsync(
        Guid actingPrincipalId,
        string justification,
        string targetResource,
        CancellationToken ct
    )
    {
        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            actingPrincipalId,
            IamPermissionKeys.UserSupportView,
            null,
            false,
            justification,
            ct,
            targetResource
        );
        if (allowed.IsFailure)
            return allowed;
        return allowed.Value
            ? Result.Success()
            : Result.Failure($"Requires {IamPermissionKeys.UserSupportView}.", "FORBIDDEN");
    }
}
