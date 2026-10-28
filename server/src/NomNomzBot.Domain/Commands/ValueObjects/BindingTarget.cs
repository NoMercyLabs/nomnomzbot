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
