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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The durable half of the security-notice pair (S-IMPERSONATION-NOTICE): every security- or
/// trust-affecting action against a channel lands here so the owner can retrieve it after the fact, not
/// only via the transient <c>DashboardHub</c> alert they may have missed entirely.
/// </summary>
public sealed class SecurityNoticeService : ISecurityNoticeService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public SecurityNoticeService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<Result<SecurityNoticeDto>> RecordAsync(
        RecordSecurityNoticeRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.NoticeType))
            return Result.Failure<SecurityNoticeDto>(
                "NoticeType is required.",
                "VALIDATION_FAILED"
            );

        if (string.IsNullOrWhiteSpace(request.Summary))
            return Result.Failure<SecurityNoticeDto>("Summary is required.", "VALIDATION_FAILED");

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        SecurityNotice notice = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = request.BroadcasterId,
            NoticeType = request.NoticeType,
            Summary = request.Summary,
            ActorPrincipalId = request.ActorPrincipalId,
            TargetUserId = request.TargetUserId,
            AccessGrantId = request.AccessGrantId,
            Reason = request.Reason,
            Scope = request.Scope,
            ExpiresAt = request.ExpiresAt,
            // Set explicitly rather than relying solely on AuditableEntityInterceptor (production wiring):
            // this row's CreatedAt IS the notice's "when it happened" timestamp the owner reads later, and
            // must be correct even under a bare DbContext (no interceptor attached) in a test harness.
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.SecurityNotices.Add(notice);
        await _db.SaveChangesAsync(ct);

        return Result.Success(ToDto(notice));
    }

    public async Task<Result<PagedList<SecurityNoticeDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<SecurityNotice> query = _db.SecurityNotices.Where(n =>
            n.BroadcasterId == broadcasterId
        );

        int total = await query.CountAsync(ct);

        List<SecurityNoticeDto> items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(n => new SecurityNoticeDto(
                n.Id,
                n.NoticeType,
                n.Summary,
                n.ActorPrincipalId,
                n.TargetUserId,
                n.AccessGrantId,
                n.Reason,
                n.Scope,
                n.ExpiresAt,
                n.CreatedAt,
                n.AcknowledgedAt,
                n.AcknowledgedByUserId
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<SecurityNoticeDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<SecurityNoticeDto>> AcknowledgeAsync(
        Guid broadcasterId,
        Guid noticeId,
        Guid acknowledgedByUserId,
        CancellationToken ct = default
    )
    {
        SecurityNotice? notice = await _db.SecurityNotices.FirstOrDefaultAsync(
            n => n.Id == noticeId && n.BroadcasterId == broadcasterId,
            ct
        );

        if (notice is null)
            return Errors.NotFound<SecurityNoticeDto>("SecurityNotice", noticeId.ToString());

        // Idempotent: acknowledging an already-acknowledged notice keeps the first acknowledgement in
        // place rather than overwriting who/when it was actually read.
        if (notice.AcknowledgedAt is null)
        {
            notice.AcknowledgedAt = _timeProvider.GetUtcNow().UtcDateTime;
            notice.AcknowledgedByUserId = acknowledgedByUserId;
            await _db.SaveChangesAsync(ct);
        }

        return Result.Success(ToDto(notice));
    }

    private static SecurityNoticeDto ToDto(SecurityNotice n) =>
        new(
            n.Id,
            n.NoticeType,
            n.Summary,
            n.ActorPrincipalId,
            n.TargetUserId,
            n.AccessGrantId,
            n.Reason,
            n.Scope,
            n.ExpiresAt,
            n.CreatedAt,
            n.AcknowledgedAt,
            n.AcknowledgedByUserId
        );
}
