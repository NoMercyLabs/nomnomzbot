// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The dashboard chat reads a channel on the signed-in user's own token (EventSub channel.chat.message needs
/// user:read:chat on that token), so a moderator sign-in must ask for it as the viewer sign-in does.
/// </summary>
public sealed class TwitchLoginScopesChatReadTests
{
    [Theory]
    [InlineData(TwitchLoginRole.Viewer)]
    [InlineData(TwitchLoginRole.Moderator)]
    [InlineData(TwitchLoginRole.Broadcaster)]
    public void Every_role_scope_set_can_read_chat(TwitchLoginRole role)
    {
        TwitchLoginScopes.For(role).Should().Contain(TwitchScopes.UserReadChat);
    }
}
