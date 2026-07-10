// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Commands.Events;

using NomNomzBot.Domain.Platform;

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

    public required string UserDisplayName { get; init; }

    public required bool Succeeded { get; init; }
}
