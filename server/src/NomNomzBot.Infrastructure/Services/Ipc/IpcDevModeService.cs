// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Ipc;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Services.Ipc;

/// <summary>
/// IPC dev-mode key registry (stream-admin.md §3.3). The secret is minted from 32 CSPRNG bytes with
/// the <c>nnzb_ipc_</c> marker, returned exactly once, and persisted only as its SHA-256 lowercase
/// hex — the same discipline as <see cref="AutomationApi.AutomationApiTokenService"/>. Connection
/// auth compares hashes with <see cref="CryptographicOperations.FixedTimeEquals"/> and refuses
/// outright when the profile is SaaS or no live key exists (dev mode off = fail closed).
/// </summary>
public sealed class IpcDevModeService : IIpcDevModeService
{
    /// <summary>Recognizable plaintext marker, matching the <c>nnzb_ak_</c> automation-token style.</summary>
    private const string KeyPrefix = "nnzb_ipc_";

    private const string SaasRefusal = "IPC dev mode is a self-host feature.";

    private readonly IApplicationDbContext _db;
    private readonly IDeploymentProfileService _profile;
    private readonly TimeProvider _clock;

    public IpcDevModeService(
        IApplicationDbContext db,
        IDeploymentProfileService profile,
        TimeProvider clock
    )
    {
        _db = db;
        _profile = profile;
        _clock = clock;
    }

    public async Task<Result<bool>> IsEnabledAsync(CancellationToken ct = default)
    {
        if (_profile.Current.Mode == DeploymentMode.Saas)
            return Result.Success(false);

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        bool anyLiveKey = await _db.IpcDevModeKeys.AnyAsync(
            k => k.DeletedAt == null && k.IsEnabled && (k.ExpiresAt == null || k.ExpiresAt > now),
            ct
        );
        return Result.Success(anyLiveKey);
    }

    public async Task<Result<IpcDevModeKeyDto>> CreateKeyAsync(
        Guid actorUserId,
        CreateIpcKeyRequest request,
        CancellationToken ct = default
    )
    {
        if (_profile.Current.Mode == DeploymentMode.Saas)
            return Result.Failure<IpcDevModeKeyDto>(SaasRefusal, "SERVICE_UNAVAILABLE");

        if (request.Label is { Length: > 100 })
            return Errors
                .ValidationFailed("Label must be 100 characters or fewer.")
                .ToTyped<IpcDevModeKeyDto>();

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
            return Errors
                .ValidationFailed("ExpiresAt must lie in the future.")
                .ToTyped<IpcDevModeKeyDto>();

        (string plaintext, string hash) = MintKey();
        IpcDevModeKey key = new()
        {
            KeyHash = hash,
            Label = request.Label,
            IsEnabled = true,
            ExpiresAt = request.ExpiresAt,
            CreatedByUserId = actorUserId,
        };
        _db.IpcDevModeKeys.Add(key);
        await _db.SaveChangesAsync(ct);

        // The ONLY place the plaintext ever leaves this service — every later read maps to null.
        return Result.Success(ToDto(key) with { PlaintextKey = plaintext });
    }

    public async Task<Result<IReadOnlyList<IpcDevModeKeyDto>>> ListKeysAsync(
        CancellationToken ct = default
    )
    {
        List<IpcDevModeKey> keys = await _db
            .IpcDevModeKeys.Where(k => k.DeletedAt == null)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<IpcDevModeKeyDto>>([.. keys.Select(ToDto)]);
    }

    public async Task<Result> RevokeKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        IpcDevModeKey? key = await _db.IpcDevModeKeys.FirstOrDefaultAsync(
            k => k.Id == keyId && k.DeletedAt == null,
            ct
        );
        if (key is null)
            return Result.Failure($"IPC dev-mode key '{keyId}' was not found.", "NOT_FOUND");

        // Tombstone, never a hard delete: the row stays as the audit trail of the key's existence.
        key.IsEnabled = false;
        key.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> AuthenticateConnectionAsync(
        string presentedKey,
        CancellationToken ct = default
    )
    {
        // Fail closed: when dev mode is off (no live key) or the profile is SaaS, no key — valid or
        // not — ever authenticates.
        Result<bool> enabled = await IsEnabledAsync(ct);
        if (enabled.IsFailure || !enabled.Value)
            return Result.Failure("IPC dev mode is not enabled.", "FORBIDDEN");

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        List<string> liveHashes = await _db
            .IpcDevModeKeys.Where(k =>
                k.DeletedAt == null && k.IsEnabled && (k.ExpiresAt == null || k.ExpiresAt > now)
            )
            .Select(k => k.KeyHash)
            .ToListAsync(ct);

        byte[] presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        bool matched = false;
        foreach (string storedHash in liveHashes)
        {
            // Constant-time compare over the full set — no early exit, so timing reveals nothing
            // about which (if any) key matched.
            matched |= CryptographicOperations.FixedTimeEquals(
                presentedHash,
                Convert.FromHexString(storedHash)
            );
        }

        return matched
            ? Result.Success()
            : Result.Failure("The presented IPC key is not valid.", "FORBIDDEN");
    }

    /// <summary>32 CSPRNG bytes behind the <c>nnzb_ipc_</c> marker; stored form is SHA-256 lowercase hex.</summary>
    private static (string Plaintext, string Hash) MintKey()
    {
        string plaintext = KeyPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
        return (plaintext, hash);
    }

    private static IpcDevModeKeyDto ToDto(IpcDevModeKey key) =>
        new(key.Id, key.Label, key.IsEnabled, key.ExpiresAt, key.CreatedAt, PlaintextKey: null);
}
