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

namespace NomNomzBot.Application.Abstractions.RateLimiting;

public interface ICooldownManager
{
    /// <summary>
    /// Checks whether the given channel/command/user combination is currently on cooldown.
    /// <paramref name="isExemptFromCooldown"/> is REQUIRED (no default) so every call site — present and
    /// future — must explicitly state whether the caller is a trusted operator (broadcaster/moderator).
    /// When true, this always returns false without touching cooldown state: trusted operators are never
    /// held by, and never extend, a command cooldown.
    /// </summary>
    bool IsOnCooldown(
        string channelId,
        string commandName,
        bool isExemptFromCooldown,
        string? userId = null
    );
    TimeSpan? GetRemainingCooldown(string channelId, string commandName, string? userId = null);
    void SetCooldown(
        string channelId,
        string commandName,
        TimeSpan duration,
        string? userId = null
    );
    void ClearCooldown(string channelId, string commandName, string? userId = null);
    void ClearAllCooldowns(string channelId);
}
