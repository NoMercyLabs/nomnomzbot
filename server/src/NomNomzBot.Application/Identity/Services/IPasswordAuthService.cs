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
/// The native email+password login — a generic account credential, not an external OAuth identity. Peer to
/// <see cref="IExternalLoginService"/>: on register/login it opens the SAME tenant-less session when the user
/// has no channel yet (<c>broadcasterId: null</c>), or resolves their existing channel when they already own
/// one. A user can hold both a password credential and linked OAuth identities at once; either signs them in.
/// Password hashing goes through <c>Microsoft.AspNetCore.Identity.PasswordHasher&lt;User&gt;</c> — never
/// hand-rolled.
/// </summary>
public interface IPasswordAuthService
{
    /// <summary>
    /// Create a new account from an email + password. Fails <c>EMAIL_TAKEN</c> when the normalized email is
    /// already registered (password OR the login email of an OAuth-linked account), <c>EMAIL_INVALID</c> for a
    /// malformed address, and <c>WEAK_PASSWORD</c> below the minimum length. On success opens a tenant-less
    /// session exactly like a first non-Twitch OAuth login — the caller has no channel yet.
    /// </summary>
    Task<Result<AuthResultDto>> RegisterAsync(
        string email,
        string password,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sign in with an email + password. Fails the single generic <c>INVALID_CREDENTIALS</c> for both an
    /// unknown email and a wrong password (never distinguishes — avoids account enumeration). Resolves the
    /// caller's existing channel (if any) into the session, exactly like a returning Twitch login.
    /// </summary>
    Task<Result<AuthResultDto>> LoginAsync(
        string email,
        string password,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    );
}
