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

namespace NomNomzBot.Application.Raids;

/// <summary>Raid counts and latest times per remote Twitch user id, split by direction.</summary>
public sealed record RaidHistory(
    IReadOnlyDictionary<string, RaidStats> Outgoing,
    IReadOnlyDictionary<string, RaidStats> Incoming
);
