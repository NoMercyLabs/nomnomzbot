// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Infrastructure.Notifications;

/// <summary>
/// No-op fallback for a host with no dashboard hub; the API host replaces it with the SignalR-backed
/// <c>ActionRequiredChangeNotifier</c>, the same pattern as the other overlay/hub notifier fallbacks.
/// </summary>
public sealed class NullActionRequiredChangeNotifier : IActionRequiredChangeNotifier
{
    public void NotifyChanged(Guid channelId) { }
}
