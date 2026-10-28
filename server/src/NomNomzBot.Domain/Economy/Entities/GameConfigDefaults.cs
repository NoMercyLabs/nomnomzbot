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

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// The one place each game setting default is written. A game is opt-in: a new game starts disabled.
/// </summary>
public static class GameConfigDefaults
{
    public const bool IsEnabled = false;
    public const bool Requires18Plus = false;
    public const int CooldownSeconds = 0;
    public const string Permission = "Everyone";
}
