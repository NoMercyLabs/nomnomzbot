// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// Pooled cross-channel savings jars (economy.md §3.7). Every mutation enforces the membership predicate — the
/// acting channel must hold an accepted membership in the jar — before any balance change (the cross-tenant
/// guard). Caps bound how much a partner can contribute or withdraw.
/// </summary>
public interface ISavingsJarService
{
    /// <summary>Creates a jar owned by the channel + its owner membership in one step.</summary>
    Task<Result<SavingsJarDto>> CreateJarAsync(
        Guid broadcasterId,
        CreateSavingsJarRequest request,
        CancellationToken ct = default
    );

    Task<Result<SavingsJarDto>> GetJarAsync(
        Guid broadcasterId,
        Guid jarId,
        CancellationToken ct = default
    );

    /// <summary>Jars this channel owns or has an accepted membership in.</summary>
    Task<Result<IReadOnlyList<SavingsJarDto>>> ListJarsForChannelAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Owner invites a partner channel (creates a pending membership). <c>JAR_MEMBERSHIP_REQUIRED</c> if not owner.</summary>
    Task<Result<SavingsJarMembershipDto>> InviteChannelAsync(
        Guid broadcasterId,
        InviteChannelRequest request,
        CancellationToken ct = default
    );

    /// <summary>The invited channel accepts its own pending membership (mutual-consent federation).</summary>
    Task<Result<SavingsJarMembershipDto>> AcceptMembershipAsync(
        Guid broadcasterId,
        Guid membershipId,
        CancellationToken ct = default
    );

    /// <summary>Owner or the member itself revokes a membership.</summary>
    Task<Result> RevokeMembershipAsync(
        Guid broadcasterId,
        Guid membershipId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Contributes a viewer's currency into the jar: verifies membership + jar open (<c>JAR_NOT_OPEN</c>) +
    /// the per-stream contribution cap (<c>JAR_CAP_EXCEEDED</c>), debits the viewer (<c>INSUFFICIENT_FUNDS</c>
    /// bubbles), increments the jar balance, records an audited movement, and fires the contribution + goal
    /// events.
    /// </summary>
    Task<Result<JarMovementDto>> ContributeAsync(
        Guid broadcasterId,
        JarContributeRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Withdraws from the jar back to a channel account: verifies membership + withdrawal caps
    /// (<c>JAR_CAP_EXCEEDED</c>) + jar balance, decrements the jar, credits the account, and records the movement.
    /// </summary>
    Task<Result<JarMovementDto>> WithdrawAsync(
        Guid broadcasterId,
        JarWithdrawRequest request,
        CancellationToken ct = default
    );

    Task<Result<PagedList<JarMovementDto>>> GetJarHistoryAsync(
        Guid broadcasterId,
        Guid jarId,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
