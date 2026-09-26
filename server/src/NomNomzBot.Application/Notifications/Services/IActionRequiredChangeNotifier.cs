// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Notifications.Services;

/// <summary>
/// Tells a channel's open dashboards that its action-required inbox may have changed, so they refetch it
/// instead of polling. Signals are coalesced per channel: a burst of changes produces one push.
/// </summary>
public interface IActionRequiredChangeNotifier
{
    /// <summary>Signals that <paramref name="channelId"/>'s inbox may have changed.</summary>
    void NotifyChanged(Guid channelId);
}
