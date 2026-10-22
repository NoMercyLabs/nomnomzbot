// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
