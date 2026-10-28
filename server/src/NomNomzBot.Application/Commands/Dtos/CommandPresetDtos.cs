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
