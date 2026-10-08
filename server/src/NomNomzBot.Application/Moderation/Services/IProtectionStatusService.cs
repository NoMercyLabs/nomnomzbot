// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
