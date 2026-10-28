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

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>
/// One button the streamer pinned to the Now playing panel. Pressing it runs <paramref name="ActionType"/> with
/// <paramref name="Parameters"/> through the channel action runner. <paramref name="Icon"/> is a dashboard icon
/// key (lowercase letters, digits and hyphens). <paramref name="Id"/> is assigned by the server when empty.
/// </summary>
public sealed record PlayerButtonDto(
    string Id,
    string Label,
    string Icon,
    string ActionType,
    Dictionary<string, JsonElement> Parameters
);
