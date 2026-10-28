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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// The check a command runs before it binds a code script from a request: the script must exist in the SAME
/// channel and not be deleted. Without it a known GUID from another channel would run that channel's script.
/// </summary>
public static class CodeScriptOwnership
{
    public const string ErrorCode = "CODE_SCRIPT_NOT_IN_CHANNEL";

    public static async Task<Result> EnsureCodeScriptInChannelAsync(
        this IApplicationDbContext db,
        Guid broadcasterId,
        Guid codeScriptId,
        CancellationToken ct = default
    )
    {
        bool owned = await db
            .CodeScripts.IgnoreQueryFilters()
            .AnyAsync(
                s =>
                    s.Id == codeScriptId && s.BroadcasterId == broadcasterId && s.DeletedAt == null,
                ct
            );
        return owned
            ? Result.Success()
            : Result.Failure("That code script does not exist in this channel.", ErrorCode);
    }
}
