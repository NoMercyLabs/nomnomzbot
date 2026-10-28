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

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A channel's configuration for one game (economy.md K.7). Gambling-category games carry the optional
/// <c>Requires18Plus</c> gate and the odds (<c>WinChancePercent</c>, <c>HouseEdgePercent</c>,
/// <c>PayoutMultiplier</c>); bet bounds, cooldown, and per-stream play limits bound abuse. One per
/// <c>(BroadcasterId, GameType)</c>.
/// </summary>
public class GameConfig : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string GameType { get; set; } = null!;
    public GameCategory Category { get; set; }
    public bool IsEnabled { get; set; } = GameConfigDefaults.IsEnabled;
    public bool Requires18Plus { get; set; } = GameConfigDefaults.Requires18Plus;
    public long? MinBet { get; set; }
    public long? MaxBet { get; set; }
    public decimal? HouseEdgePercent { get; set; }
    public decimal? WinChancePercent { get; set; }
    public decimal? PayoutMultiplier { get; set; }
    public int CooldownSeconds { get; set; } = GameConfigDefaults.CooldownSeconds;
    public int? MaxPlaysPerStream { get; set; }
    public string? ConfigJson { get; set; }
    public string Permission { get; set; } = GameConfigDefaults.Permission;
}
