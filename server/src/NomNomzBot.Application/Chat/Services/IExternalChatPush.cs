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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>Delivers an off-bot chat event to exactly one dashboard connection.</summary>
public interface IExternalChatPush
{
    Task PushAsync(
        string connectionId,
        ExternalChatEvent chatEvent,
        CancellationToken ct = default
    );
}
