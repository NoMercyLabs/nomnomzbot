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
