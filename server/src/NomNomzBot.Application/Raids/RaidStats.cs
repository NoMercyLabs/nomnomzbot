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

/// <summary>
/// Raid history with one remote channel in one direction: how many raids happened (<see cref="Count"/>)
/// and when the latest one did (<see cref="LastAt"/>). Used both ways (us to them, them to us) so the
/// scorer can tell who owes whom a raid.
/// </summary>
public sealed record RaidStats(int Count, DateTimeOffset LastAt);
