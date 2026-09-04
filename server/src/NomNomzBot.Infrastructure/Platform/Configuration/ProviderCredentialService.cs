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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Infrastructure.Platform.Configuration;

/// <summary>
/// Reads and writes the platform's OAuth app credentials for the operator console, over the same
/// system-scoped <c>Configuration</c> rows the setup wizard writes and
/// <see cref="SystemCredentialsProvider"/> resolves.
///
/// <para>It deliberately re-uses that provider's own AAD (<see cref="SystemCredentialsProvider.ContextFor"/>)
/// rather than sealing its own way: a value saved here has to be openable by the OAuth flows, and two
/// definitions of the same AAD is how that quietly stops being true.</para>
/// </summary>
public sealed class ProviderCredentialService : IProviderCredentialService
{
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ITokenProtector _protector;
    private readonly ISystemCredentialsProvider _credentials;

    public ProviderCredentialService(
        IApplicationDbContext db,
        IConfiguration configuration,
        ITokenProtector protector,
        ISystemCredentialsProvider credentials
    )
    {
        _db = db;
        _configuration = configuration;
        _protector = protector;
        _credentials = credentials;
    }

    /// <summary>
    /// The providers whose app credentials this build can actually use. Ordered as an operator meets them:
    /// the streaming platforms first, then the integrations.
    /// </summary>
    public static readonly IReadOnlyList<string> Providers =
    [
        "twitch",
        "kick",
        "youtube",
        "twitter",
        "spotify",
        "discord",
        "patreon",
        "shopify",
        "treatstream",
    ];

    public async Task<Result<IReadOnlyList<ProviderCredentialDto>>> ListAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ProviderCredentialDto> rows = [];
        foreach (string provider in Providers)
            rows.Add(await DescribeAsync(provider, cancellationToken));

        return Result.Success<IReadOnlyList<ProviderCredentialDto>>(rows);
    }

    public async Task<Result<ProviderCredentialDto>> SaveAsync(
        string provider,
        SaveProviderCredentialRequest request,
        CancellationToken cancellationToken = default
    )
    {
        string? normalized = Normalize(provider);
        if (normalized is null)
            return Errors.NotFound<ProviderCredentialDto>("Provider", provider);

        if (
            string.IsNullOrWhiteSpace(request.ClientId)
            && string.IsNullOrWhiteSpace(request.ClientSecret)
        )
            return Result.Failure<ProviderCredentialDto>(
                "Nothing to save — supply a client id, a client secret, or both.",
                "VALIDATION_FAILED"
            );

        if (!string.IsNullOrWhiteSpace(request.ClientId))
            await UpsertAsync(
                $"{normalized}.client_id",
                request.ClientId.Trim(),
                secure: false,
                cancellationToken
            );

        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            await UpsertAsync(
                $"{normalized}.client_secret",
                request.ClientSecret.Trim(),
                secure: true,
                cancellationToken
            );

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(normalized, cancellationToken));
    }

    public async Task<Result<ProviderCredentialDto>> ClearAsync(
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        string? normalized = Normalize(provider);
        if (normalized is null)
            return Errors.NotFound<ProviderCredentialDto>("Provider", provider);

        string[] keys = [$"{normalized}.client_id", $"{normalized}.client_secret"];
        List<Domain.Platform.Entities.Configuration> stored = await _db
            .Configurations.Where(c => c.BroadcasterId == null && keys.Contains(c.Key))
            .ToListAsync(cancellationToken);

        // A hard delete, not a soft one: a soft-deleted row that the resolver still reads would leave the
        // operator staring at "cleared" while the stale secret kept winning — the exact confusion this
        // whole surface exists to end.
        _db.Configurations.RemoveRange(stored);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(await DescribeAsync(normalized, cancellationToken));
    }

    private async Task<ProviderCredentialDto> DescribeAsync(
        string provider,
        CancellationToken cancellationToken
    )
    {
        string section = ConfigSectionFor(provider);

        string? storedId = await ReadStoredAsync($"{provider}.client_id", cancellationToken);
        string? envId = _configuration[$"{section}:ClientId"];

        string? storedSecret = await ReadStoredAsync(
            $"{provider}.client_secret",
            cancellationToken
        );
        string? envSecret = _configuration[$"{section}:ClientSecret"];

        return new(
            Provider: provider,
            // The RESOLVED id — what the OAuth flows will actually send — not merely the stored one, so the
            // operator reads the value in play rather than the value they last typed.
            ClientId: Coalesce(storedId, envId),
            ClientIdSource: SourceOf(storedId, envId),
            SecretSource: SourceOf(storedSecret, envSecret),
            AppDecisionRecorded: await _credentials.IsAppDecisionRecordedAsync(
                provider,
                cancellationToken
            ),
            Supported: true
        );
    }

    /// <summary>
    /// Reads a stored row's value, opening a sealed one under the resolver's own AAD. A sealed value that
    /// will not open (crypto-shredded, or stale after an ENCRYPTION_KEY rotation) reads as absent here for
    /// the same reason the resolver treats it as absent: it cannot be used, so reporting it as configured
    /// would be a lie the operator then has to debug.
    /// </summary>
    private async Task<string?> ReadStoredAsync(string key, CancellationToken cancellationToken)
    {
        Domain.Platform.Entities.Configuration? row = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == null && c.Key == key,
            cancellationToken
        );

        if (row is null)
            return null;

        if (string.IsNullOrEmpty(row.SecureValue))
            return string.IsNullOrWhiteSpace(row.Value) ? null : row.Value;

        return await _protector.TryUnprotectAsync(
            row.SecureValue,
            SystemCredentialsProvider.ContextFor(key),
            cancellationToken
        );
    }

    private async Task UpsertAsync(
        string key,
        string value,
        bool secure,
        CancellationToken cancellationToken
    )
    {
        Domain.Platform.Entities.Configuration? row = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == null && c.Key == key,
            cancellationToken
        );

        if (row is null)
        {
            row = new() { BroadcasterId = null, Key = key };
            _db.Configurations.Add(row);
        }

        if (secure)
        {
            row.SecureValue = await _protector.ProtectAsync(
                value,
                SystemCredentialsProvider.ContextFor(key),
                cancellationToken
            );
            // Never leave a previous plaintext behind beside the sealed value — the resolver prefers the
            // sealed one, but a reader that did not would hand back the old secret in the clear.
            row.Value = null;
        }
        else
        {
            row.Value = value;
        }
    }

    private static string SourceOf(string? stored, string? environment) =>
        !string.IsNullOrWhiteSpace(stored) ? CredentialSource.Stored
        : !string.IsNullOrWhiteSpace(environment) ? CredentialSource.Environment
        : CredentialSource.Unset;

    private static string? Coalesce(string? stored, string? environment) =>
        !string.IsNullOrWhiteSpace(stored) ? stored
        : !string.IsNullOrWhiteSpace(environment) ? environment
        : null;

    private static string? Normalize(string provider)
    {
        string candidate = provider.Trim().ToLowerInvariant();
        return Providers.Contains(candidate) ? candidate : null;
    }

    private static string ConfigSectionFor(string provider) =>
        provider switch
        {
            "youtube" => "YouTube",
            _ => char.ToUpperInvariant(provider[0]) + provider[1..],
        };
}
