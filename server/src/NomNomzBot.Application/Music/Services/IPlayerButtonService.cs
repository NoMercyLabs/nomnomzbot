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
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The action buttons a streamer pins to the Now playing panel, stored per channel in the Configuration
/// key-value store (key <c>music:player-buttons</c>, no schema of its own).
/// </summary>
public interface IPlayerButtonService
{
    /// <summary>The channel's buttons in display order; an empty list when none are set.</summary>
    Task<Result<IReadOnlyList<PlayerButtonDto>>> GetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Replaces the whole list. Fails with <c>VALIDATION_FAILED</c> when there are more than
    /// <see cref="PlayerButtonRules.MaxButtons"/> buttons, a label is longer than
    /// <see cref="PlayerButtonRules.MaxLabelLength"/> (an empty label is allowed and stored as empty: the
    /// button then shows only its icon), an icon key is not lowercase letters, digits and hyphens,
    /// or an action type is empty. Buttons without an id get one.
    /// </summary>
    Task<Result<IReadOnlyList<PlayerButtonDto>>> ReplaceAsync(
        Guid broadcasterId,
        IReadOnlyList<PlayerButtonDto> buttons,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The limits <see cref="IPlayerButtonService.ReplaceAsync"/> enforces.</summary>
public static class PlayerButtonRules
{
    public const int MaxButtons = 12;
    public const int MaxLabelLength = 24;
}
