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
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The platform-wide trust &amp; safety desk (S-ADMIN-8a). Every entry point gates and audits FIRST through
/// <see cref="IPlatformIamService.AuthorizePlatformAsync"/> — the same funnel <c>AdminSupportService</c>
/// uses — naming the acting operator and the detection/actor touched before a single cross-tenant row is
/// read or reversed.
/// </summary>
public sealed class TrustSafetyReviewService(
    IApplicationDbContext db,
    IPlatformIamService iam,
    IModerationService moderation,
    TimeProvider time
) : ITrustSafetyReviewService
{
    public async Task<Result<IReadOnlyList<CrossTenantAbuseSignalDto>>> GetCrossTenantSignalsAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            "cross-tenant-signals",
            ct
        );
        if (gate.IsFailure)
            return gate.WithValue<IReadOnlyList<CrossTenantAbuseSignalDto>>(null!);

        List<SpamDetection> detections = await db
            .SpamDetections.IgnoreQueryFilters()
            .Where(d => d.DeletedAt == null)
            .ToListAsync(ct);

        List<IGrouping<(string Provider, string SubjectPlatformUserId), SpamDetection>> byActor =
            detections
                .GroupBy(d => (d.Provider, d.SubjectPlatformUserId))
                .Where(g => g.Select(d => d.BroadcasterId).Distinct().Count() >= 2)
                .ToList();

        if (byActor.Count == 0)
            return Result.Success<IReadOnlyList<CrossTenantAbuseSignalDto>>([]);

        List<Guid> broadcasterIds = byActor
            .SelectMany(g => g.Select(d => d.BroadcasterId))
            .Distinct()
            .ToList();
        Dictionary<Guid, string> channelNames = await ChannelNamesAsync(broadcasterIds, ct);

        List<CrossTenantAbuseSignalDto> signals = byActor
            .Select(g =>
            {
                List<SpamDetection> ordered = g.OrderByDescending(d => d.DetectedAt).ToList();
                List<CrossTenantAbuseHitDto> hits = ordered
                    .Select(d => new CrossTenantAbuseHitDto(
                        d.BroadcasterId,
                        channelNames.GetValueOrDefault(d.BroadcasterId, "unknown channel"),
                        d.Id,
                        d.Confidence,
                        d.Outcome,
                        d.Reason,
                        d.DetectedAt
                    ))
                    .ToList();

                return new CrossTenantAbuseSignalDto(
                    g.Key.Provider,
                    g.Key.SubjectPlatformUserId,
                    ordered[0].SubjectDisplayName,
                    hits.Select(h => h.BroadcasterId).Distinct().Count(),
                    hits.Count,
                    hits
                );
            })
            .OrderByDescending(s => s.TenantCount)
            .ThenByDescending(s => s.DetectionCount)
            .ToList();

        return Result.Success<IReadOnlyList<CrossTenantAbuseSignalDto>>(signals);
    }

    public async Task<Result<PagedList<TrustSafetyReviewItemDto>>> GetReviewQueueAsync(
        Guid actingPrincipalId,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(actingPrincipalId, justification, "review-queue", ct);
        if (gate.IsFailure)
            return gate.WithValue<PagedList<TrustSafetyReviewItemDto>>(null!);

        IQueryable<SpamDetection> automatic = AutomaticActionRows();

        int total = await automatic.CountAsync(ct);
        List<SpamDetection> page = await automatic
            .OrderByDescending(d => d.DetectedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        Dictionary<Guid, string> channelNames = await ChannelNamesAsync(
            page.Select(d => d.BroadcasterId).Distinct().ToList(),
            ct
        );

        List<TrustSafetyReviewItemDto> items = page.Select(d =>
                ToReviewItem(d, channelNames.GetValueOrDefault(d.BroadcasterId, "unknown channel"))
            )
            .ToList();

        return Result.Success(
            new PagedList<TrustSafetyReviewItemDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result> ConfirmAsync(
        Guid actingPrincipalId,
        Guid detectionId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            $"confirm:{detectionId}",
            ct
        );
        if (gate.IsFailure)
            return gate;

        SpamDetection? detection = await AutomaticActionRows()
            .FirstOrDefaultAsync(d => d.Id == detectionId, ct);
        if (detection is null)
            return Result.Failure("No pending automatic action with that id.", "NOT_FOUND");
        if (detection.OverturnedAt is not null)
            return Result.Failure("Already overturned; nothing to confirm.", "VALIDATION_FAILED");
        if (detection.ConfirmedAt is not null)
            return Result.Failure("Already confirmed.", "VALIDATION_FAILED");

        detection.ConfirmedAt = time.GetUtcNow().UtcDateTime;
        detection.ConfirmedByUserId = actingPrincipalId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> OverturnAsync(
        Guid actingPrincipalId,
        Guid detectionId,
        string justification,
        CancellationToken ct = default
    )
    {
        Result gate = await RequireAsync(
            actingPrincipalId,
            justification,
            $"overturn:{detectionId}",
            ct
        );
        if (gate.IsFailure)
            return gate;

        SpamDetection? detection = await AutomaticActionRows()
            .FirstOrDefaultAsync(d => d.Id == detectionId, ct);
        if (detection is null)
            return Result.Failure("No pending automatic action with that id.", "NOT_FOUND");
        if (detection.OverturnedAt is not null)
            return Result.Failure("Already overturned.", "VALIDATION_FAILED");

        Guid ownerUserId = await db
            .Channels.Where(c => c.Id == detection.BroadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty)
            return Result.Failure(
                "The channel that took this action no longer exists.",
                "NOT_FOUND"
            );

        // The reversal is attempted BEFORE anything on the row is stamped: a failed unban must leave the
        // detection exactly as it was, never claiming a reversal the platform could not actually make.
        Result reversal = await moderation.UnbanAsync(
            detection.BroadcasterId.ToString(),
            ownerUserId,
            detection.SubjectPlatformUserId,
            actingPrincipalId.ToString(),
            ct
        );
        if (reversal.IsFailure)
            return Result.Failure(
                $"Could not reverse the automatic action: {reversal.ErrorMessage}",
                "REVERSAL_FAILED"
            );

        detection.OverturnedAt = time.GetUtcNow().UtcDateTime;
        detection.OverturnedByUserId = actingPrincipalId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// The automatic-account-action universe this desk reviews: enforcement (not dry run) that escalated
    /// to the account, not merely a message deletion or a flag.
    /// </summary>
    private IQueryable<SpamDetection> AutomaticActionRows() =>
        db
            .SpamDetections.IgnoreQueryFilters()
            .Where(d =>
                d.DeletedAt == null && !d.WasDryRun && d.Outcome == SpamOutcome.DeleteAndEscalate
            );

    private static TrustSafetyReviewItemDto ToReviewItem(SpamDetection d, string channelName) =>
        new(
            d.Id,
            d.BroadcasterId,
            channelName,
            d.SubjectPlatformUserId,
            d.SubjectDisplayName,
            d.Provider,
            d.MessageText,
            d.Signals,
            d.Confidence,
            d.Outcome,
            d.Reason,
            d.DetectedAt,
            d.ConfirmedAt,
            d.ConfirmedByUserId,
            d.OverturnedAt,
            d.OverturnedByUserId,
            ReversalPreviewFor(d, channelName)
        );

    private static string ReversalPreviewFor(SpamDetection d, string channelName) =>
        d.OverturnedAt is not null
            ? "Already reversed."
            : $"Removes the automatic Twitch timeout issued against {d.SubjectDisplayName} in {channelName}.";

    private async Task<Dictionary<Guid, string>> ChannelNamesAsync(
        List<Guid> broadcasterIds,
        CancellationToken ct
    ) =>
        await db
            .Channels.IgnoreQueryFilters()
            .Where(c => broadcasterIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    private async Task<Result> RequireAsync(
        Guid actingPrincipalId,
        string justification,
        string targetResource,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(justification))
            return Result.Failure(
                "A justification is required for a cross-tenant trust & safety action.",
                "VALIDATION_FAILED"
            );

        Result<bool> allowed = await iam.AuthorizePlatformAsync(
            actingPrincipalId,
            IamPermissionKeys.TrustSafetyReview,
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
            : Result.Failure($"Requires {IamPermissionKeys.TrustSafetyReview}.", "FORBIDDEN");
    }
}
