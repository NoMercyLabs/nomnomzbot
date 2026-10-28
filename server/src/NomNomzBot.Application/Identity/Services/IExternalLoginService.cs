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
