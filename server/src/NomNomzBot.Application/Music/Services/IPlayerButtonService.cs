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
