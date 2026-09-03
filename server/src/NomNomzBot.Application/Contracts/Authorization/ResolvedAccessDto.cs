// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
/// <remarks>
/// Every rung on this record is a NAME. They used to be numbers, and two different kinds of number at
/// that: <c>EffectiveLevel</c>/<c>CommunityLevel</c>/<c>ManagementLevel</c> were unified-ladder values
/// (0/2/4/6/10/20/30/40), while <c>CommunityStanding</c>/<c>ManagementRole</c>/<c>PermitRole</c> serialized
/// as enum ORDINALS (Moderator=0, LeadModerator=1, …) — so the same concept arrived as 10 in one field and
/// 0 in another, and inserting a single enum member would silently renumber every consumer. The client had
/// to keep hand-written ordinal tables to read them back.
///
/// <para><c>EffectiveLevel</c> is the MAX of the three planes; <c>ManagementRole</c> is null for a caller
/// who holds no management role.</para>
/// </remarks>
public sealed record ResolvedAccessDto(
    Guid UserId,
    Guid BroadcasterId,
    string EffectiveLevel,
    string CommunityStanding,
    string CommunityLevel,
    string? ManagementRole,
    string ManagementLevel,
    string? PermitRole,
    IReadOnlyList<string> PermitCapabilities,
    string WinningSource,
    IReadOnlyList<string> HeldActionKeys
);
