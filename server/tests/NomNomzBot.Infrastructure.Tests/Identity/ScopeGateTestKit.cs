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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Notifications.Sources;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The real scope gate over a real test database: a Twitch connection holding the granted scopes, the real
/// scope-notification service that records the gap, and the real inbox source that reads it back. Nothing here
/// re-implements product logic, so a test through it proves what a streamer would see.
/// </summary>
internal sealed class ScopeGateTestKit(AuthDbContext db)
{
    public TwitchScopeGateService Gate { get; } =
        new(
            db,
            new ScopeNotificationService(
                db,
                new ServiceCollection().BuildServiceProvider(),
                new(),
                TimeProvider.System,
                NullLogger<ScopeNotificationService>.Instance
            )
        );

    /// <summary>A gate that holds every scope, for tests whose subject is not the scope check.</summary>
    public static ITwitchScopeGateService OpenGate()
    {
        ITwitchScopeGateService gate = Substitute.For<ITwitchScopeGateService>();
        gate.HasScopeOrRecordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return gate;
    }

    public async Task GrantAsync(Guid broadcasterId, params string[] scopes)
    {
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = broadcasterId,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                Status = AuthEnums.IntegrationStatus.Connected,
                Scopes = [.. scopes],
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>The scope names the channel's action-required inbox currently lists as missing.</summary>
    public async Task<List<string>> InboxScopesAsync(Guid broadcasterId)
    {
        List<ActionRequiredItemDto> items = (
            await new TwitchGrantGapSource(db).GetItemsAsync(broadcasterId, new HashSet<string>())
        ).Value;
        return
        [
            .. items
                .Where(i => i.Kind == "twitch_scope_missing")
                .Select(i => i.Parameters["scope"]),
        ];
    }
}
