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
    DateTime? LastSyncedAt,
    bool IsOwner
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
