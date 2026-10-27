// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Marks an event that records a change somebody made, so the event store stamps the acting principal on its
/// journal row. Facts that arrive from a platform (chat, EventSub) do not carry this marker: nobody on this
/// side "did" them.
/// </summary>
public interface IActorAttributedEvent : IDomainEvent;
