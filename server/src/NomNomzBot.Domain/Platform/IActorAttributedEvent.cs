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

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Marks an event that records a change somebody made, so the event store stamps the acting principal on its
/// journal row. Facts that arrive from a platform (chat, EventSub) do not carry this marker: nobody on this
/// side "did" them.
/// </summary>
public interface IActorAttributedEvent : IDomainEvent;
