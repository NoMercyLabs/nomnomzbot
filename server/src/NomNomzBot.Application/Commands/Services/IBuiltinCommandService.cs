// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Manages per-channel enable/disable state and overrides for built-in commands.
/// Absent row = enabled with catalog defaults.
/// </summary>
public interface IBuiltinCommandService
{
    /// <summary>Returns all built-ins for a channel, merging catalog defaults with stored overrides.</summary>
    Task<Result<IReadOnlyList<BuiltinCommandDto>>> ListAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Enables or disables a built-in for the channel. Upserts the toggle row.</summary>
    Task<Result> SetEnabledAsync(
        string broadcasterId,
        string builtinKey,
        bool enabled,
        CancellationToken ct = default
    );
}

public sealed record BuiltinCommandDto(
    string BuiltinKey,
    string Name,
    bool IsEnabled,
    int DefaultCooldownSeconds,
    int DefaultMinPermissionLevel
);
