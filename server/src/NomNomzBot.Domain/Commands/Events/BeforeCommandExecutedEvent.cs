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

public sealed class BeforeCommandExecutedEvent : DomainEventBase
{
    public required string CommandName { get; init; }
    public required string TriggeredByUserId { get; init; }
    public required string TriggeredByDisplayName { get; init; }
    public required string MessageId { get; init; }
    public required string RawMessage { get; init; }
}
