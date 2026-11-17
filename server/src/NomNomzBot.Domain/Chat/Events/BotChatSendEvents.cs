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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// Twitch refused (403) the bot's chat message in a channel, on every token the bot can send with. The usual
/// cause is a bot that is not a moderator there, or a channel that did not grant it. Published once per
/// channel per hour, so the action-required inbox can tell the channel how to fix it without a flood.
/// </summary>
public sealed class BotChatSendRefusedEvent : DomainEventBase
{
    /// <summary>The Twitch login of the bot whose message was refused.</summary>
    public required string BotUsername { get; init; }
}

/// <summary>
/// The bot's chat message went through in a channel where Twitch had refused it. Published once, on the first
/// success after a <see cref="BotChatSendRefusedEvent"/>, so the inbox item clears by itself.
/// </summary>
public sealed class BotChatSendRecoveredEvent : DomainEventBase;
