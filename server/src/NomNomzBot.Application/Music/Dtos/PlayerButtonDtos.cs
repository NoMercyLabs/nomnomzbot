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
