// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// CRUD over the channel's keyword chat triggers ("someone says X → the bot reacts"). Every write
/// refreshes the in-process hot-path cache, so a change is live without a restart; a regex pattern is
/// compile-checked at write time so an invalid one never reaches the matcher.
/// </summary>
public interface IChatTriggerService
{
    Task<Result<IReadOnlyList<ChatTriggerDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    Task<Result<ChatTriggerDto>> CreateAsync(
        string broadcasterId,
        CreateChatTriggerRequest request,
        CancellationToken cancellationToken = default
    );

    Task<Result<ChatTriggerDto>> UpdateAsync(
        string broadcasterId,
        Guid triggerId,
        UpdateChatTriggerRequest request,
        CancellationToken cancellationToken = default
    );

    Task<Result> DeleteAsync(
        string broadcasterId,
        Guid triggerId,
        CancellationToken cancellationToken = default
    );
}
