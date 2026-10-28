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

namespace NomNomzBot.Infrastructure.Dashboard.Replay;

/// <summary>
/// One step of a replay: the activity-feed id (which keys its captured overlay alerts) and the domain event
/// rebuilt from the journal — null when the journal holds no readable event for it (e.g. legacy imports).
/// </summary>
public sealed record ActivityReplayItem(string ChannelEventId, IDomainEvent? Event);
