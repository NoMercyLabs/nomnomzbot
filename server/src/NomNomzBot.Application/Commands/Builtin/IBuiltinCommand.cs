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

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// A single code-defined built-in command. Implementations live in Infrastructure and are
/// registered in DI; the catalog is built from the resolved set at startup.
/// Built-in catalog membership is defined by code, never by DB seed rows.
/// </summary>
public interface IBuiltinCommand
{
    string BuiltinKey { get; }
    int DefaultCooldownSeconds { get; }
    int DefaultMinPermissionLevel { get; }

    Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    );
}

/// <summary>Runtime invocation context for a built-in command execution.</summary>
public sealed class BuiltinCommandContext
{
    public required Guid BroadcasterId { get; init; }
    public required string TriggeringUserId { get; init; }
    public required string TriggeringUserDisplayName { get; init; }

    /// <summary>Twitch login (lowercase) of the caller — the get-or-create viewer-resolution key.</summary>
    public string TriggeringUserLogin { get; init; } = string.Empty;

    /// <summary>
    /// The caller's live badge level on the unified ladder (roles-permissions §0) — builtins that enforce
    /// a standing floor (e.g. a game's Permission) pass it through instead of re-resolving.
    /// </summary>
    public int RoleLevel { get; init; }

    /// <summary>Arguments after the command trigger (e.g. "!followage @someone" → "@someone").</summary>
    public string Args { get; init; } = string.Empty;

    /// <summary>Optional template override from ChannelBuiltinCommand.OverridesJson.</summary>
    public string? CustomResponseTemplate { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
