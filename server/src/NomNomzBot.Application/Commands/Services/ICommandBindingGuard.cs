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
