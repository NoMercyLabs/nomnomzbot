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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The fun-command preset pack (<c>!8ball</c>, <c>!hug</c>, …): seeds it onto a channel and resets a seeded
/// command back to its preset.
/// </summary>
public interface ICommandPresetService
{
    /// <summary>Every preset, as a reset would write it.</summary>
    IReadOnlyList<CommandPresetDto> ListPresets();

    /// <summary>
    /// Seeds every preset the channel does not have yet. Safe to repeat: a name the channel already uses is
    /// left alone, and a preset the channel deleted is never brought back.
    /// </summary>
    Task<Result<CommandPresetSeedReport>> SeedAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Writes the preset back over a seeded command, keeping its name and on/off state. Fails with
    /// <c>NOT_A_PRESET</c> when the command was written by the channel itself.
    /// </summary>
    Task<Result<CommandDto>> ResetAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    );
}
