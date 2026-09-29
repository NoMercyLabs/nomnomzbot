// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Dtos;

/// <summary>
/// One fun-command preset exactly as a reset writes it back — every field a reset restores, so the dashboard
/// can show the streamer what changes before they confirm. The command's own name and its on/off state are
/// never part of a reset (the streamer may have renamed or switched it off on purpose).
/// </summary>
public sealed record CommandPresetDto(
    string Key,
    string Tier,
    string? Description,
    string? TemplateResponse,
    List<string> TemplateResponses,
    string MinPermissionLevel,
    int CooldownSeconds,
    int UserCooldownSeconds,
    bool CooldownPerUser,
    List<string> Aliases,
    string PrefixMode,
    string MatchMode
);

/// <summary>What one fun-pack seed pass did for a channel.</summary>
public sealed record CommandPresetSeedReport(
    int Seeded,
    int AlreadyPresent,
    int DeletedByChannel,
    IReadOnlyList<string> Failed
);
