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

namespace NomNomzBot.Domain.Commands.Events;

using Platform;

/// <summary>
/// Published by the chat hot path after every command execution — builtin, template response, and
/// pipeline tier alike. The single command-execution fact: the dashboard hub broadcast, the command
/// use-count, and the analytics projections (<c>CommandsRun</c>, per-viewer <c>CommandCount</c>) all
/// fold from this one event.
/// </summary>
public sealed class CommandExecutedEvent : DomainEventBase
{
    /// <summary>The canonical command name (alias-resolved for custom commands).</summary>
    public required string CommandName { get; init; }

    /// <summary>Twitch user id of the caller.</summary>
    public required string UserId { get; init; }

    /// <summary>Twitch login of the caller.</summary>
    public required string Username { get; init; }

    /// <summary>The display name of the caller, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>True when the command ran without an error.</summary>
    public required bool Succeeded { get; init; }
}
