// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
