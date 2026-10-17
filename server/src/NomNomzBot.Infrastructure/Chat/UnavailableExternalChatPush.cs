// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
