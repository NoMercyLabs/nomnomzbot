// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The channel's built-in reply catalogue (commands-pipelines.md §11): every reply slot of every built-in, what
/// it says for this channel right now and where that text comes from, plus the per-slot write and reset.
/// </summary>
public interface IBuiltinReplyService
{
    /// <summary>Every reply group with its slots, resolved for the channel's current personality.</summary>
    Task<Result<IReadOnlyList<BuiltinReplyGroupDto>>> ListAsync(
        string broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sets the channel's own text for exactly one slot. A blank template resets the slot. Rejects an unknown
    /// slot (NOT_FOUND), a locked slot, a template over 500 characters or one naming an unknown variable
    /// (VALIDATION_FAILED). Returns the slot as it now resolves.
    /// </summary>
    Task<Result<BuiltinReplyDto>> SetAsync(
        string broadcasterId,
        string builtinKey,
        string slot,
        string? template,
        CancellationToken ct = default
    );

    /// <summary>Removes the channel's own text for one slot; returns the slot back on its default.</summary>
    Task<Result<BuiltinReplyDto>> ResetAsync(
        string broadcasterId,
        string builtinKey,
        string slot,
        CancellationToken ct = default
    );
}
