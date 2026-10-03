// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Events;

/// <summary>When a chat command fails to run.</summary>
public sealed class CommandFailedEvent : DomainEventBase
{
    /// <summary>The name of the command that failed.</summary>
    public required string CommandName { get; init; }

    /// <summary>The Twitch user id of the viewer who ran the command, a number as text.</summary>
    public required string TriggeredByUserId { get; init; }

    /// <summary>A short text that explains why the command failed.</summary>
    public required string Reason { get; init; }
}
