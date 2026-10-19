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

/// <summary>One row on a binding surface that runs a command, shown in the blast-radius list.</summary>
/// <param name="Surface">Stable surface key, lower case (for example "reward", "timer", "event-response").</param>
/// <param name="Id">The bound row's id, as text.</param>
/// <param name="Name">The label the owner sees for the row.</param>
public sealed record BindingUsage(string Surface, string Id, string Name);

/// <summary>
/// The failure data of a refused command delete or disable: the rows that still run the command.
/// The API writes it into the 409 response <c>data</c> (code <c>COMMAND_IN_USE</c>).
/// </summary>
/// <param name="CommandName">The command the owner tried to delete or disable.</param>
/// <param name="Usages">One entry per bound row, from every surface.</param>
public sealed record CommandInUse(string CommandName, IReadOnlyList<BindingUsage> Usages)
    : IResponseErrorData;
