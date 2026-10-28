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
