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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Reads "can we talk to Twitch as the bot yet" off the canonical token vault by delegating to the exact path
/// every bot-scoped Twitch call uses — <see cref="ITwitchTokenResolver.GetBotTokenAsync"/>. That resolves the
/// shared platform bot <c>IntegrationConnection</c> (Provider <c>twitch_bot</c>, no broadcaster) and decrypts
/// its token; success is precisely the predicate that lets the EventSub transport, the IRC connection, and the
/// Helix warmers do real work. Reusing the resolver keeps a single fact (no parallel query that could drift):
/// the same call that fails with "No bot token is configured" is the one that flips this gate. Scoped — it
/// rides the resolver's per-request DbContext + vault, so singleton hosted loops resolve it inside a scope.
/// </summary>
public sealed class PlatformBotReadinessGate(ITwitchTokenResolver tokenResolver)
    : IPlatformBotReadinessGate
{
    public async Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct = default)
    {
        Result<TwitchAccessContext> token = await tokenResolver.GetBotTokenAsync(ct);
        return token.IsSuccess;
    }
}
