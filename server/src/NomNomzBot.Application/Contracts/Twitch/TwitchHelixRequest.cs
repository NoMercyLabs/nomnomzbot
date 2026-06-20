// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Which bearer a Helix call rides on. <see cref="App"/> uses the bot/app token (subject-agnostic
/// reads); <see cref="User"/> uses the broadcaster's user token (tenant-scoped reads/writes).
/// </summary>
public enum TwitchHelixAuth
{
    App = 0,
    User = 1,
}

/// <summary>
/// One DTO-agnostic Helix request: HTTP method, the path under <c>/helix</c>, the query, an optional
/// JSON body, the auth kind, and the call priority. The per-endpoint (codegen-fed) methods build one of
/// these and hand it to <see cref="ITwitchHelixTransport"/> — the request shape is the only coupling
/// between the hand-written methods and the shared pipeline.
/// </summary>
/// <param name="Method">HTTP verb (GET / POST / PATCH / DELETE).</param>
/// <param name="Path">Path under the Helix base, no leading slash (e.g. <c>users</c>, <c>moderation/bans</c>).</param>
/// <param name="Auth">App/bot token vs broadcaster user token.</param>
/// <param name="BroadcasterId">Tenant whose user token is used and whose Twitch id resolves the request; null for app-token calls.</param>
/// <param name="Query">Query parameters appended to the URL (values URL-encoded by the transport).</param>
/// <param name="Body">Optional object serialised as the JSON request body; null for bodyless calls.</param>
/// <param name="Priority">Two-band rate-limiter priority; user-triggered calls jump background polls.</param>
public sealed record TwitchHelixRequest(
    HttpMethod Method,
    string Path,
    TwitchHelixAuth Auth,
    Guid? BroadcasterId = null,
    IReadOnlyList<KeyValuePair<string, string>>? Query = null,
    object? Body = null,
    TwitchCallPriority Priority = TwitchCallPriority.Background
);
