// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Warns before an ad break, like the old bot (AdScheduleService). While a channel is live and its broadcaster
/// token has the ads scope, the channel's Helix ad schedule is read about once a minute. Each pass then publishes
/// the countdown events to the overlays and, once per next-ad slot at about 3 minutes, tells chat.
/// </summary>
public interface IAdScheduleWarner
{
    /// <summary>One pass over every live channel: poll the schedule when it is due, then warn from the cached schedule.</summary>
    Task ProcessAsync(CancellationToken cancellationToken);
}
