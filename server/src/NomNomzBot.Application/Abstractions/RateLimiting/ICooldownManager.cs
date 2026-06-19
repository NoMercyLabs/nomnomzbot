// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.RateLimiting;

public interface ICooldownManager
{
    bool IsOnCooldown(string channelId, string commandName, string? userId = null);
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
