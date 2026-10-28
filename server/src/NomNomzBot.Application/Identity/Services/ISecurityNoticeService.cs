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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Durable, per-tenant record of security- or trust-affecting platform actions (S-IMPERSONATION-NOTICE).
/// The transient <c>DashboardHub</c> alert is the live path; this is the recoverable one — the affected
/// channel owner can retrieve it after the fact, whether or not they were online when the action happened.
/// </summary>
public interface ISecurityNoticeService
{
    /// <summary>Persists one durable notice for the affected channel. Never throws on a missing channel.</summary>
    Task<Result<SecurityNoticeDto>> RecordAsync(
        RecordSecurityNoticeRequest request,
        CancellationToken ct = default
    );

    /// <summary>Lists every past notice for the channel, newest first — the owner's full review surface.</summary>
    Task<Result<PagedList<SecurityNoticeDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Marks one notice acknowledged by <paramref name="acknowledgedByUserId"/>. Idempotent — acknowledging
    /// an already-acknowledged notice keeps the original acknowledgement. Fails <c>NOT_FOUND</c> when the
    /// notice does not belong to <paramref name="broadcasterId"/> (tenant scoping).
    /// </summary>
    Task<Result<SecurityNoticeDto>> AcknowledgeAsync(
        Guid broadcasterId,
        Guid noticeId,
        Guid acknowledgedByUserId,
        CancellationToken ct = default
    );
}
