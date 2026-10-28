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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Answers "is protection running?" for a channel by joining the subsystems that decide it: the bot's
/// moderator role, spam-defence mode, whether automatic action can reach each connected platform, and the
/// moderation EventSub subscriptions. Read-only; a check it cannot read is reported unknown, never ok.
/// </summary>
public interface IProtectionStatusService
{
    Task<Result<ProtectionStatusDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );
}
