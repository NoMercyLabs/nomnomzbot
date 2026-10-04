// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Stream;

/// <summary>
/// Records every inbox signal together with the persisted <c>Channel.IsLive</c> at the moment of the signal,
/// read fresh from the database the way the inbox sources read it.
/// </summary>
internal sealed class LiveStateCapturingNotifier(AuthDbContext db) : IActionRequiredChangeNotifier
{
    public List<(Guid ChannelId, bool PersistedIsLive)> Calls { get; } = [];

    public void NotifyChanged(Guid channelId)
    {
        bool isLive = db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => c.IsLive)
            .Single();
        Calls.Add((channelId, isLive));
    }
}
