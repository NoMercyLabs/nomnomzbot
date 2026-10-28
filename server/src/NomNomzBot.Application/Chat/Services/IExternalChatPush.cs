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
