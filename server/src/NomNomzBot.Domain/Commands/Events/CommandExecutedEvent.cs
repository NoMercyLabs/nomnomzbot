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

public sealed class CommandExecutedEvent : DomainEventBase
{
    public required string ChannelId { get; init; }
    public required string CommandName { get; init; }
    public required string UserId { get; init; }
    public required string Username { get; init; }
}
