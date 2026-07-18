// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// <see cref="IScriptStorageService"/> over the tenant-scoped <see cref="Storage"/> table (unique on
/// (Key, BroadcasterId)). Every script key is stored under the <c>script:</c> prefix so the script surface can
/// never read, overwrite, or enumerate a non-script storage row, and the per-channel key cap counts script keys
/// only. All reads/writes filter on the channel's <c>BroadcasterId</c> explicitly — channel B can never see
/// channel A's state.
/// </summary>
public sealed class ScriptStorageService(IApplicationDbContext db) : IScriptStorageService
{
    private const string KeyPrefix = "script:";

    public async Task<string?> GetAsync(
        Guid broadcasterId,
        string key,
        CancellationToken ct = default
    )
    {
        if (!TryPrefix(key, out string storageKey))
            return null;

        Storage? row = await db.Storages.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Key == storageKey,
            ct
        );
        return row?.Value;
    }

    public async Task<Result> SetAsync(
        Guid broadcasterId,
        string key,
        string value,
        CancellationToken ct = default
    )
    {
        if (!TryPrefix(key, out string storageKey))
            return Result.Failure(
                $"Storage key must be 1–{IScriptStorageService.MaxKeyLength} characters.",
                "VALIDATION_FAILED"
            );
        if (Encoding.UTF8.GetByteCount(value) > IScriptStorageService.MaxValueBytes)
            return Result.Failure(
                $"Storage value exceeds {IScriptStorageService.MaxValueBytes} bytes.",
                "VALIDATION_FAILED"
            );

        Storage? row = await db.Storages.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Key == storageKey,
            ct
        );
        if (row is null)
        {
            // The key cap only gates NEW keys (script keys only — the prefix filter keeps other storage
            // uses out of the count), so an existing key can always be rewritten.
            int keyCount = await db.Storages.CountAsync(
                s => s.BroadcasterId == broadcasterId && s.Key.StartsWith(KeyPrefix),
                ct
            );
            if (keyCount >= IScriptStorageService.MaxKeysPerChannel)
                return Result.Failure(
                    $"Storage is full: this channel already holds {IScriptStorageService.MaxKeysPerChannel} script keys.",
                    "LIMIT_EXCEEDED"
                );

            db.Storages.Add(
                new Storage
                {
                    BroadcasterId = broadcasterId,
                    Key = storageKey,
                    Value = value,
                }
            );
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        string key,
        CancellationToken ct = default
    )
    {
        if (!TryPrefix(key, out string storageKey))
            return Result.Failure(
                $"Storage key must be 1–{IScriptStorageService.MaxKeyLength} characters.",
                "VALIDATION_FAILED"
            );

        Storage? row = await db.Storages.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Key == storageKey,
            ct
        );
        if (row is not null)
        {
            db.Storages.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return Result.Success(); // idempotent — deleting an absent key is not an error
    }

    public async Task<IReadOnlyList<string>> ListAsync(
        Guid broadcasterId,
        string? prefix = null,
        CancellationToken ct = default
    )
    {
        string storagePrefix = KeyPrefix + (prefix ?? string.Empty);
        List<string> keys = await db
            .Storages.Where(s =>
                s.BroadcasterId == broadcasterId && s.Key.StartsWith(storagePrefix)
            )
            .Select(s => s.Key)
            .OrderBy(k => k)
            .ToListAsync(ct);
        return [.. keys.Select(k => k[KeyPrefix.Length..])];
    }

    // A valid user key is non-blank and within the length cap; the stored key carries the script namespace.
    private static bool TryPrefix(string key, out string storageKey)
    {
        storageKey = string.Empty;
        if (string.IsNullOrWhiteSpace(key) || key.Length > IScriptStorageService.MaxKeyLength)
            return false;
        storageKey = KeyPrefix + key;
        return true;
    }
}
