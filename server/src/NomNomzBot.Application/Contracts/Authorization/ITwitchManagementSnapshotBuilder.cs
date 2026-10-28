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

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Builds a channel's management snapshot from Twitch (roles-permissions §4) — its moderators (badge-sourced)
/// and channel editors (Helix editors) — resolving each Twitch user to a local <c>User</c> Guid. Shared by the
/// onboarding seed and the periodic reconcile so the two never drift. Reports WHICH sources it could actually
/// read (<see cref="ManagementSnapshot.AuthoritativeSources"/>): a source whose Twitch read failed is absent, so
/// the reconciler can prune only the sources it trusts this run and never wipe roles on a transient Twitch error.
/// </summary>
public interface ITwitchManagementSnapshotBuilder
{
    Task<ManagementSnapshot> BuildAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>
/// A freshly-read Twitch management snapshot: the members plus the set of sources whose read SUCCEEDED (only
/// those may be pruned against, so a failed moderator/editor read leaves those rows intact).
/// </summary>
public sealed record ManagementSnapshot(
    IReadOnlyList<TwitchManagementMember> Members,
    IReadOnlySet<MembershipSource> AuthoritativeSources
);
