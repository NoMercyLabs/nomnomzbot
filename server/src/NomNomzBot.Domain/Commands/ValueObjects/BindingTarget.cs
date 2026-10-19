// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Domain.Commands.ValueObjects;

/// <summary>
/// The one thing a binding runs (reward, timer, event response, chat trigger, automation token, ...).
/// <paramref name="Id"/> names the command or pipeline; an <see cref="BindingTargetKind.Action"/> names its
/// <paramref name="ActionType"/> and carries its <paramref name="Params"/> instead.
/// </summary>
public sealed record BindingTarget(
    BindingTargetKind Kind,
    string? Id = null,
    string? ActionType = null,
    IReadOnlyDictionary<string, JsonElement>? Params = null
);
