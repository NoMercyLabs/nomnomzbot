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
using NomNomzBot.Application.DTOs.Ipc;

namespace NomNomzBot.Application.Services;

/// <summary>
/// The opt-in local-IPC dev-mode key registry + connection authenticator (stream-admin.md §3.3).
/// Off by default and never honored for remote callers — dev mode is on only when the deployment
/// profile is NOT SaaS and at least one enabled, unexpired, non-deleted key exists. Only the SHA-256
/// hash of a key is ever stored; the plaintext appears exactly once, in the create response.
/// The socket listener itself is a separate Infrastructure hosted service that authenticates each
/// connection through <see cref="AuthenticateConnectionAsync"/>.
/// </summary>
public interface IIpcDevModeService
{
    /// <summary>True only when <c>DeploymentProfile.Mode != saas</c> AND at least one non-deleted,
    /// non-expired key has <c>IsEnabled=true</c>; off by default.</summary>
    Task<Result<bool>> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>Mints a random key, stores its SHA-256 hash (never plaintext) + label/expiry, and
    /// returns the plaintext ONCE in <see cref="IpcDevModeKeyDto.PlaintextKey"/> (null on every
    /// later read). Refused (<c>SERVICE_UNAVAILABLE</c>) on the SaaS profile.</summary>
    Task<Result<IpcDevModeKeyDto>> CreateKeyAsync(
        Guid actorUserId,
        CreateIpcKeyRequest request,
        CancellationToken ct = default
    );

    /// <summary>Metadata only (id, label, enabled, expiry, created) — never key material.</summary>
    Task<Result<IReadOnlyList<IpcDevModeKeyDto>>> ListKeysAsync(CancellationToken ct = default);

    /// <summary>Soft-deletes (tombstones) the key and disables it; <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> RevokeKeyAsync(Guid keyId, CancellationToken ct = default);

    /// <summary>
    /// Local-socket auth: constant-time compare (<c>CryptographicOperations.FixedTimeEquals</c>) of the
    /// presented key's SHA-256 against every live key hash. Success only for an enabled, unexpired key;
    /// <c>FORBIDDEN</c> otherwise. Refuses outright when dev mode is off or the profile is SaaS —
    /// never authenticates in those states.
    /// </summary>
    Task<Result> AuthenticateConnectionAsync(string presentedKey, CancellationToken ct = default);
}
