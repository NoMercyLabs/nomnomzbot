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

using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Stand-in for a container without the API host (composition tests): there is no dashboard connection to
/// push to, so nothing is delivered. The API host registers the hub-backed pusher after AddInfrastructure.
/// </summary>
internal sealed class UnavailableExternalChatPush : IExternalChatPush
{
    public Task PushAsync(
        string connectionId,
        ExternalChatEvent chatEvent,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
