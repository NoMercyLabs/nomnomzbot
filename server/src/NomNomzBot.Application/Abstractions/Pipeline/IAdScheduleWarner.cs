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
