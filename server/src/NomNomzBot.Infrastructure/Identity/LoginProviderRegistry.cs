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
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The login-provider descriptor registry (platform-identity §3.2). Providers are data: Twitch is always on;
/// YouTube, Kick and Twitter/X are registered but light up only when BOTH their platform feature flag is on
/// AND their OAuth app credentials are configured — so the login screen never shows a button whose flow would
/// 503 for lack of a client id ("no broken buttons"), and enabling one is "register the app + set the creds +
/// flip the flag". Registered as a singleton (the descriptor list is static); the feature-flag and credential
/// lookups resolve through a fresh scope to avoid capturing the scoped services.
/// </summary>
public sealed class LoginProviderRegistry : ILoginProviderRegistry
{
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly IReadOnlyList<LoginProviderDescriptor> Descriptors =
    [
        new(
            Key: AuthEnums.LoginProvider.Twitch,
            DisplayName: "Twitch",
            SupportedFlows: LoginFlows.DeviceCode | LoginFlows.AuthCode,
            FeatureFlagKey: "", // always on — the shipped login provider
            // Any user token can read its own Get Users row; we never store an email, so we never ask for one.
            LoginScopes: []
        ),
        new(
            Key: AuthEnums.LoginProvider.YouTube,
            DisplayName: "YouTube",
            SupportedFlows: LoginFlows.DeviceCode | LoginFlows.AuthCode,
            FeatureFlagKey: "use_youtube_login",
            LoginScopes: ["openid", "profile"]
        ),
        new(
            Key: AuthEnums.LoginProvider.Kick,
            DisplayName: "Kick",
            SupportedFlows: LoginFlows.AuthCodePkce,
            FeatureFlagKey: "use_kick_login",
            LoginScopes: ["user:read"]
        ),
        // Login-only (never owns a Channel, platform-identity §10.1). Auth-code + PKCE; no device grant.
        new(
            Key: AuthEnums.LoginProvider.Twitter,
            DisplayName: "Twitter / X",
            SupportedFlows: LoginFlows.AuthCodePkce,
            FeatureFlagKey: "use_twitter_login",
            LoginScopes: ["users.read", "tweet.read", "offline.access"]
        ),
    ];

    public LoginProviderRegistry(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public IReadOnlyList<LoginProviderDescriptor> All => Descriptors;

    public async Task<IReadOnlyList<LoginProviderDescriptor>> EnabledAsync(
        CancellationToken cancellationToken = default
    )
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IFeatureFlagService flags = scope.ServiceProvider.GetRequiredService<IFeatureFlagService>();
        ISystemCredentialsProvider credentials =
            scope.ServiceProvider.GetRequiredService<ISystemCredentialsProvider>();

        List<LoginProviderDescriptor> enabled = [];
        foreach (LoginProviderDescriptor descriptor in Descriptors)
        {
            // Twitch is the shipped, always-on provider — its client id is a required boot credential.
            if (string.IsNullOrEmpty(descriptor.FeatureFlagKey))
            {
                enabled.Add(descriptor);
                continue;
            }

            // Platform-level gate (no tenant on the login screen) — the platform sentinel channel.
            bool flagOn = await flags.IsEnabledForAsync(
                descriptor.FeatureFlagKey,
                Guid.Empty,
                cancellationToken
            );
            if (!flagOn)
                continue;

            // A flag can only be honoured once the provider's OAuth app credentials actually exist; without a
            // client id the authorize/device flow returns SERVICE_UNAVAILABLE, so we must not surface the button.
            string? clientId = await credentials.GetClientIdAsync(
                descriptor.Key,
                cancellationToken
            );
            if (string.IsNullOrEmpty(clientId))
                continue;

            enabled.Add(descriptor);
        }

        return enabled;
    }

    public Result<LoginProviderDescriptor> Get(string key)
    {
        string normalized = key.ToLowerInvariant();
        LoginProviderDescriptor? descriptor = Descriptors.FirstOrDefault(d => d.Key == normalized);
        return descriptor is null
            ? Result.Failure<LoginProviderDescriptor>(
                $"Unknown login provider '{key}'.",
                "UNKNOWN_PROVIDER"
            )
            : Result.Success(descriptor);
    }
}
