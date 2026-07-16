// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// The registry of OAuth provider descriptors (integrations-oauth §3.2). Holds the Spotify + YouTube
/// descriptors (endpoints + the full manageable scope-set surface) and resolves credentials per deployment:
/// SaaS uses the platform app credentials from config; a self-host operator's own
/// <c>{Provider}:ClientId/ClientSecret</c> is BYOK. A new ordinary OAuth2 provider plugs in as one more
/// descriptor here — no new controller or service.
/// </summary>
public sealed class OAuthProviderRegistry : IOAuthProviderRegistry
{
    private readonly IConfiguration _configuration;

    public OAuthProviderRegistry(IConfiguration configuration) => _configuration = configuration;

    public IReadOnlyList<string> KnownProviders { get; } =
    [
        AuthEnums.IntegrationProvider.Spotify,
        AuthEnums.IntegrationProvider.YouTube,
        AuthEnums.IntegrationProvider.Kick,
        AuthEnums.IntegrationProvider.Patreon,
    ];

    public Result<OAuthProviderDescriptor> Resolve(string provider, Guid broadcasterId)
    {
        string key = provider.ToLowerInvariant();
        return key switch
        {
            AuthEnums.IntegrationProvider.Spotify => Result.Success(Spotify()),
            AuthEnums.IntegrationProvider.YouTube => Result.Success(YouTube()),
            AuthEnums.IntegrationProvider.Kick => Result.Success(Kick()),
            AuthEnums.IntegrationProvider.Patreon => Result.Success(Patreon()),
            _ => Result.Failure<OAuthProviderDescriptor>(
                $"Unknown OAuth provider '{provider}'.",
                "UNKNOWN_PROVIDER"
            ),
        };
    }

    private OAuthProviderDescriptor Spotify() =>
        new(
            Provider: AuthEnums.IntegrationProvider.Spotify,
            AuthorizeEndpoint: "https://accounts.spotify.com/authorize",
            TokenEndpoint: "https://accounts.spotify.com/api/token",
            RevokeEndpoint: null, // Spotify has no token-revocation endpoint.
            AccountIdentityEndpoint: "https://api.spotify.com/v1/me",
            UsesPkce: true,
            ScopeSets: new Dictionary<string, IReadOnlyList<string>>
            {
                ["spotify.playback"] =
                [
                    "user-read-playback-state",
                    "user-modify-playback-state",
                    "user-read-currently-playing",
                ],
                ["spotify.library"] =
                [
                    "playlist-read-private",
                    "playlist-modify-public",
                    "playlist-modify-private",
                    "user-library-read",
                    "user-library-modify",
                ],
            },
            IsByok: ResolveIsByok("Spotify")
        );

    private OAuthProviderDescriptor Kick() =>
        new(
            Provider: AuthEnums.IntegrationProvider.Kick,
            AuthorizeEndpoint: "https://id.kick.com/oauth/authorize",
            TokenEndpoint: "https://id.kick.com/oauth/token",
            RevokeEndpoint: "https://id.kick.com/oauth/revoke",
            AccountIdentityEndpoint: "https://api.kick.com/public/v1/users",
            UsesPkce: true,
            ScopeSets: new Dictionary<string, IReadOnlyList<string>>
            {
                // The streamer-plane grant the Kick chat platform needs (slice 3b-2c; live docs
                // 2026-07-11): send + moderation + the webhook event subscription the chat READ rides.
                ["kick.chat"] =
                [
                    "user:read",
                    "chat:write",
                    "moderation:ban",
                    "moderation:chat_message:manage",
                    "events:subscribe",
                ],
            },
            IsByok: ResolveIsByok("Kick")
        );

    private OAuthProviderDescriptor Patreon() =>
        new(
            Provider: AuthEnums.IntegrationProvider.Patreon,
            AuthorizeEndpoint: "https://www.patreon.com/oauth2/authorize",
            TokenEndpoint: "https://www.patreon.com/api/oauth2/token",
            RevokeEndpoint: null, // Patreon documents no token-revocation endpoint.
            AccountIdentityEndpoint: "https://www.patreon.com/api/oauth2/v2/identity",
            UsesPkce: false, // confidential client (client secret); Patreon documents no PKCE support.
            ScopeSets: new Dictionary<string, IReadOnlyList<string>>
            {
                // The supporter-events ingest core (supporter-events.md): who the creator is, their
                // campaign, its members, and webhook management — w:campaigns.webhook lets the bot
                // register the members/pledge webhooks itself instead of a manual portal trip.
                ["patreon.supporters"] =
                [
                    "identity",
                    "campaigns",
                    "campaigns.members",
                    "w:campaigns.webhook",
                ],
                // Member PII (emails/addresses) is its own opt-in set — never bundled into the core grant.
                ["patreon.members_pii"] = ["campaigns.members[email]", "campaigns.members.address"],
                ["patreon.posts"] = ["campaigns.posts"],
                ["patreon.lives"] = ["campaigns.lives", "w:campaigns.lives"],
            },
            IsByok: ResolveIsByok("Patreon")
        );

    private OAuthProviderDescriptor YouTube() =>
        new(
            Provider: AuthEnums.IntegrationProvider.YouTube,
            AuthorizeEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint: "https://oauth2.googleapis.com/token",
            RevokeEndpoint: "https://oauth2.googleapis.com/revoke",
            AccountIdentityEndpoint: "https://www.googleapis.com/oauth2/v3/userinfo",
            UsesPkce: true,
            ScopeSets: new Dictionary<string, IReadOnlyList<string>>
            {
                ["youtube.manage"] = ["https://www.googleapis.com/auth/youtube"],
                ["youtube.readonly"] = ["https://www.googleapis.com/auth/youtube.readonly"],
            },
            IsByok: ResolveIsByok("YouTube")
        );

    /// <summary>
    /// The deployment BYOK flag: a self-host operator's own <c>{Provider}:Byok</c> marks bring-your-own-key.
    /// The client_id/secret themselves are resolved per request by <c>ISystemCredentialsProvider</c> (vaulted
    /// store → config); per-tenant BYOK overrides ride <c>IntegrationConnection.IsByok</c> at connect time.
    /// </summary>
    private bool ResolveIsByok(string configSection) =>
        !string.IsNullOrWhiteSpace(_configuration[$"{configSection}:Byok"]);
}
