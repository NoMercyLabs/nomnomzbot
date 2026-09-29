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

namespace NomNomzBot.Infrastructure.Dashboard.Replay;

/// <summary>
/// One step of a replay: the activity-feed id (which keys its captured overlay alerts) and the domain event
/// rebuilt from the journal — null when the journal holds no readable event for it (e.g. legacy imports).
/// </summary>
public sealed record ActivityReplayItem(string ChannelEventId, IDomainEvent? Event);
