// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// The full breakdown of a caller's resolved authorization for a channel (roles-permissions §4) — each
/// plane's contributing level and the winning source — for the permissions UI and debugging.
/// <c>EffectiveLevel</c> is <c>MAX(CommunityLevel, ManagementLevel, permit-role level)</c>.
/// </summary>
public sealed record ResolvedAccessDto(
    Guid UserId,
    Guid BroadcasterId,
    int EffectiveLevel,
    CommunityStanding CommunityStanding,
    int CommunityLevel,
    ManagementRole? ManagementRole,
    int ManagementLevel,
    ManagementRole? PermitRole,
    IReadOnlyList<string> PermitCapabilities,
    string WinningSource
);
