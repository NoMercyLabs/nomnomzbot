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

/// <summary>A channel-management membership row for the dashboard roles screen (roles-permissions §4).</summary>
public sealed record ChannelMembershipDto(
    Guid Id,
    Guid UserId,
    string? Username,
    ManagementRole Role,
    int LevelValue,
    MembershipSource Source,
    Guid? GrantedByUserId,
    DateTime GrantedAt,
    DateTime? LastSyncedAt
);

/// <summary>
/// One management member in a freshly-fetched Twitch snapshot fed into membership reconciliation
/// (roles-permissions §4) — built by the Twitch integration subsystem from mod/editor badges + Helix editors.
/// </summary>
public sealed record TwitchManagementMember(
    Guid UserId,
    string TwitchUserId,
    ManagementRole Role,
    MembershipSource Source
);
