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
