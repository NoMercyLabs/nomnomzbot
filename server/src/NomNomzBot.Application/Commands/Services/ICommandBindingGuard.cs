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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The one blast-radius check for a bound command. It gathers every <see cref="IBindingUsageSource"/>.
/// </summary>
public interface ICommandBindingGuard
{
    /// <summary>Failure code of a refused delete or disable.</summary>
    const string InUseCode = "COMMAND_IN_USE";

    /// <summary>All rows that bind the command, from every surface.</summary>
    Task<IReadOnlyList<BindingUsage>> FindUsagesAsync(
        Guid broadcasterId,
        string commandTargetId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Success when nothing binds the command. Otherwise a <c>COMMAND_IN_USE</c> failure whose
    /// <see cref="Result.ErrorData"/> is a <see cref="CommandInUse"/> with the full list.
    /// </summary>
    Task<Result> EnsureUnboundAsync(
        Guid broadcasterId,
        string commandTargetId,
        string commandName,
        CancellationToken ct = default
    );
}
