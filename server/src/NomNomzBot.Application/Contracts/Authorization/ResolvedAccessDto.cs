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
/// <para>
/// <c>PermitCapabilities</c> are the caller's per-USER capability grants only. <c>HeldActionKeys</c> is the
/// broader, UI-facing set: EVERY action key in the catalogue the caller actually CLEARS on this channel —
/// their <c>EffectiveLevel</c> meets the action's channel-effective required level (which FOLDS IN the
/// broadcaster's <c>ChannelActionOverride</c>, unlike the per-plane level fields), OR they hold a direct
/// per-user capability grant for it. The dashboard gates page/action visibility on this set.
/// </para>
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
    string WinningSource,
    IReadOnlyList<string> HeldActionKeys
);
