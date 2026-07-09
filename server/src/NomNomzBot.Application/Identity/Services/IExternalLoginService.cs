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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// A proven external identity (platform-identity §3.1) — produced ONLY by a login provider's OAuth handler
/// (device poll / code exchange) after the provider's tokens are vaulted, never from client input.
/// <see cref="ConnectionId"/> points at the vaulted user-level login connection (may be null when the caller
/// did not vault a connection, e.g. an identify-only login).
/// </summary>
public sealed record ExternalIdentityProof(
    string Provider,
    string ProviderUserId,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    Guid? ConnectionId
);

/// <summary>
/// Turns a proven external identity into a platform session — the generic, platform-agnostic login
/// (platform-identity §3.3). Resolves/creates the user + primary identity, links the vaulted login connection,
/// enriches the profile, and issues a TENANT-LESS session: a brand-new non-Twitch user has no channel yet, so
/// they land on the channel picker. Twitch keeps its own richer streamer-session path (which also onboards the
/// owner's channel); this is the path every OTHER provider funnels through.
/// </summary>
public interface IExternalLoginService
{
    Task<Result<AuthResultDto>> LoginAsync(
        ExternalIdentityProof proof,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );
}
