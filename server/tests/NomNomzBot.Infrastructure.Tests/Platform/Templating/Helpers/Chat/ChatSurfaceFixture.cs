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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

/// <summary>The chat namespace's channel modes, pinned message and shared session.</summary>
internal static class ChatSurfaceFixture
{
    public static void Configure(ServiceCollection services, Guid channel)
    {
        ITwitchChatApi chat = (ITwitchChatApi)
            services.Last(d => d.ServiceType == typeof(ITwitchChatApi)).ImplementationInstance!;
        chat.GetChatSettingsAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchChatSettings(
                        "555001",
                        EmoteMode: false,
                        FollowerMode: true,
                        FollowerModeDuration: 10,
                        ModeratorId: null,
                        NonModeratorChatDelay: false,
                        NonModeratorChatDelayDuration: null,
                        SlowMode: true,
                        SlowModeWaitTime: 3,
                        SubscriberMode: false,
                        UniqueChatMode: false
                    )
                )
            );
        DateTimeOffset now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
        chat.GetPinnedMessagesAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPinnedChatMessage(
                        "pin-1",
                        "Welcome to the stream",
                        now,
                        null,
                        true,
                        false
                    )
                )
            );

        ITwitchChatAssetsApi assets = (ITwitchChatAssetsApi)
            services
                .Last(d => d.ServiceType == typeof(ITwitchChatAssetsApi))
                .ImplementationInstance!;
        assets
            .GetSharedChatSessionAsync(channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchSharedChatSession(
                        "shared-1",
                        "555001",
                        [new("555001"), new("2002")],
                        now,
                        now
                    )
                )
            );

        ISharedChatSessionTracker tracker = Substitute.For<ISharedChatSessionTracker>();
        tracker
            .GetActiveSession(channel)
            .Returns(new SharedChatSessionInfo("shared-1", "555001", ["555001", "2002"]));
        services.AddSingleton(tracker);
    }
}
