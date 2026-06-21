// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// The minimal Twitch user profile read during the OAuth login/bot flows (identity-auth §3.1) — fetched
/// directly from <c>GET /helix/users</c> with the freshly-exchanged token, before it is vaulted, so it does
/// not ride the vaulted-token Helix sub-clients. Distinct from the canonical <c>TwitchUser</c> Helix DTO,
/// which serves the post-login, tenant-scoped reads.
/// </summary>
public record TwitchUserInfo(
    string Id,
    string Login,
    string DisplayName,
    string? ProfileImageUrl,
    string BroadcasterType,
    string Type = "",
    DateTime? AccountCreatedAt = null
);
