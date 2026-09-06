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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The network-wide block desk (S-ADMIN-8b) — the most dangerous control in the product, because it acts
/// across EVERY tenant of this deployment at once. Every entry point requires <c>network:block:manage</c>
/// AND a justification, and gates/audits through <c>IPlatformIamService.AuthorizePlatformAsync</c> exactly
/// like the sibling S-ADMIN-8a desk. The dangerous capability is deliberately its own system role, never
/// bundled into a broad platform role — it must be assigned to a NAMED operator.
/// </summary>
public interface INetworkBlockService
{
    /// <summary>
    /// The real, freshly-computed blast radius a block against this actor would touch — required before
    /// <see cref="ApplyAsync"/> will accept a confirmation.
    /// </summary>
    Task<Result<NetworkBlockPreviewDto>> PreviewAsync(
        Guid actingPrincipalId,
        string targetTwitchUserId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// Applies the block: recomputes the blast radius fresh and REJECTS (<c>PREVIEW_STALE</c>) if it no
    /// longer matches <see cref="ApplyNetworkBlockRequest.ConfirmedTenantCount"/> — the operator must see
    /// the real numbers again before acting on them. On success, bans the actor on every live tenant
    /// channel's own token (the same best-effort fan-out <c>NetworkNukeService</c> uses) and persists the
    /// durable network-wide deny flag every Gate-2 check now reads.
    /// </summary>
    Task<Result<NetworkBlockDto>> ApplyAsync(
        Guid actingPrincipalId,
        ApplyNetworkBlockRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lifts a network block: unbans the actor on every tenant channel the original apply actioned before
    /// touching the row. The row is stamped fully <c>lifted</c> ONLY when every leg actually restores —
    /// a partial outcome records the real counts and the failed channel ids honestly and stays enforced.
    /// </summary>
    Task<Result<NetworkBlockDto>> LiftAsync(
        Guid actingPrincipalId,
        Guid blockId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>Every network block, newest first — active/partial ones show they are still enforced.</summary>
    Task<Result<IReadOnlyList<NetworkBlockDto>>> ListAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken ct = default
    );
}
