// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Platform.Configuration;

/// <summary>
/// The one place the platform's OAuth app credentials are resolved (the onboarding keystone). Reads the
/// wizard-vaulted system <c>Configuration</c> rows (<c>BroadcasterId == null</c>, <c>Key = "{provider}.client_id"</c>
/// plain / <c>"{provider}.client_secret"</c> sealed) FIRST, then falls back to <c>{Provider}:ClientId/ClientSecret</c>
/// from <see cref="IConfiguration"/> (env / appsettings). Every secret is sealed/opened under the AAD
/// <c>("system", provider, field)</c> — a sealed value for one provider can never be opened as another's, and a
/// raw DB read yields only sealed bytes. Scoped: it reads the per-request <see cref="IApplicationDbContext"/>.
/// </summary>
public sealed class SystemCredentialsProvider(
    IApplicationDbContext db,
    ITokenProtector protector,
    IConfiguration configuration
) : ISystemCredentialsProvider
{
    public async Task<SystemAppCredentials?> GetAsync(
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        string section = ConfigSectionFor(provider);

        string? clientId =
            await ReadConfigAsync($"{provider}.client_id", cancellationToken)
            ?? configuration[$"{section}:ClientId"];
        string? clientSecret =
            await ReadConfigAsync($"{provider}.client_secret", cancellationToken)
            ?? configuration[$"{section}:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return null;

        return new SystemAppCredentials(clientId, clientSecret);
    }

    public async Task<string?> GetValueAsync(
        string provider,
        string field,
        CancellationToken cancellationToken = default
    )
    {
        string? value = await ReadConfigAsync($"{provider}.{field}", cancellationToken);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        string section = ConfigSectionFor(provider);
        return configuration[$"{section}:{PascalCase(field)}"];
    }

    /// <summary>
    /// Reads one system-scoped <c>Configuration</c> row: a sealed <see cref="ConfigEntity.SecureValue"/> is
    /// opened under the row's AAD; otherwise the plain <see cref="ConfigEntity.Value"/> is returned. Null when
    /// the row is absent or the sealed value fails to open (crypto-shredded / tampered) — never throws.
    /// </summary>
    public async Task<string?> ReadConfigAsync(string key, CancellationToken cancellationToken)
    {
        ConfigEntity? cfg = await db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == null && c.Key == key,
            cancellationToken
        );
        if (cfg is null)
            return null;

        return cfg.SecureValue is not null
            ? await protector.TryUnprotectAsync(cfg.SecureValue, ContextFor(key), cancellationToken)
            : cfg.Value;
    }

    /// <summary>
    /// The AAD for a system-scoped secret: subject <c>"system"</c>, the provider, and the field. Binding each
    /// sealed value to its provider + field is what stops a sealed <c>spotify.client_secret</c> being opened as
    /// <c>twitch</c>'s. Mirrors the key shape the setup endpoints write (<c>"{provider}.{field}"</c>).
    /// </summary>
    public static TokenProtectionContext ContextFor(string key)
    {
        int dot = key.IndexOf('.');
        return new TokenProtectionContext(
            "system",
            dot > 0 ? key[..dot] : "system",
            dot > 0 ? key[(dot + 1)..] : key
        );
    }

    // appsettings sections are PascalCase (Twitch / Spotify / YouTube / Discord); the provider tokens are
    // lower-case. youtube → YouTube is the one non-titlecase mapping.
    private static string ConfigSectionFor(string provider) =>
        provider switch
        {
            "youtube" => "YouTube",
            _ => PascalCase(provider),
        };

    private static string PascalCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
