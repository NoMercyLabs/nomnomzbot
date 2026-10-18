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
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Platform.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a saved platform default reaches every open platform defaults page with the set and the key.</summary>
public sealed class AdminPlatformDefaultsBroadcastHandlerTests
{
    [Fact]
    public async Task Saved_default_pushes_the_set_and_the_key()
    {
        IHubContext<AdminHub, IAdminClient> hub = Substitute.For<
            IHubContext<AdminHub, IAdminClient>
        >();
        IAdminClient all = Substitute.For<IAdminClient>();
        hub.Clients.All.Returns(all);
        object? payload = null;
        all.ReceivePlatformDefaultsChanged(Arg.Do<object>(p => payload = p))
            .Returns(Task.CompletedTask);

        await new AdminPlatformDefaultsBroadcastHandler(hub).HandleAsync(
            new PlatformDefaultsChangedEvent { Set = "actions", Key = "chat.timeout" }
        );

        payload.Should().NotBeNull();
        Type t = payload!.GetType();
        t.GetProperty("Set")!.GetValue(payload).Should().Be("actions");
        t.GetProperty("Key")!.GetValue(payload).Should().Be("chat.timeout");
    }
}
