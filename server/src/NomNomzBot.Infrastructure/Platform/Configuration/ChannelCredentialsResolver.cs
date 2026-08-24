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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Platform.Configuration;

/// <summary>
/// BYOC resolution for a channel's own OAuth app credentials — mirrors <see cref="SystemCredentialsProvider"/>'s
/// mechanism exactly (a <c>Configuration</c> row, <c>"{provider}.client_id"</c> plain / <c>"{provider}.client_secret"</c>
/// sealed) but scoped per channel (<c>BroadcasterId == channelId</c> instead of <c>null</c>). The AAD subject is
/// <c>"channel:{channelId}"</c> — distinct from the system rows' <c>"system"</c> subject — so a channel-sealed
/// secret can never be opened as the system's, or another channel's.
/// </summary>
public sealed class ChannelCredentialsResolver(
    IServiceScopeFactory scopeFactory,
    ISystemCredentialsProvider systemCredentials
) : IChannelCredentialsResolver
{
    public async Task<Result<SystemAppCredentials>> ResolveAsync(
        Guid channelId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        SystemAppCredentials? channelOwn = await ReadChannelCredentialsAsync(
            channelId,
            provider,
            cancellationToken
        );
        if (channelOwn is not null)
            return Result.Success(channelOwn);

        SystemAppCredentials? appLevel = await systemCredentials.GetAsync(
            provider,
            cancellationToken
        );
        if (appLevel is not null)
            return Result.Success(appLevel);

        return Result.Failure<SystemAppCredentials>(
            $"{provider} app credentials are not configured. Connect the app credentials in "
                + "Settings, or add the channel's own client id and secret.",
            "PROVIDER_NOT_CONFIGURED"
        );
    }

    /// <summary>
    /// Reads the channel-scoped credential row pair. BOTH fields must resolve — a stray client id with no
    /// secret (or vice-versa) is treated as not-configured at the channel scope and falls through to the
    /// app-level credentials, rather than issuing a half-formed OAuth request.
    /// </summary>
    private async Task<SystemAppCredentials?> ReadChannelCredentialsAsync(
        Guid channelId,
        string provider,
        CancellationToken cancellationToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITokenProtector protector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();

        List<ConfigEntity> rows = await db
            .Configurations.Where(c =>
                c.BroadcasterId == channelId
                && (c.Key == $"{provider}.client_id" || c.Key == $"{provider}.client_secret")
            )
            .ToListAsync(cancellationToken);

        string? clientId = rows.FirstOrDefault(r => r.Key == $"{provider}.client_id")?.Value;
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        ConfigEntity? secretRow = rows.FirstOrDefault(r => r.Key == $"{provider}.client_secret");
        if (secretRow?.SecureValue is null)
            return null;

        string? clientSecret = await protector.TryUnprotectAsync(
            secretRow.SecureValue,
            ContextFor(channelId, provider),
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(clientSecret))
            return null;

        return new(clientId, clientSecret);
    }

    /// <summary>The channel-scoped AAD: subject <c>"channel:{channelId}"</c> — never <c>"system"</c> — so a
    /// sealed channel secret can never be opened under the system provider's context, or another channel's.</summary>
    public static TokenProtectionContext ContextFor(Guid channelId, string provider) =>
        new($"channel:{channelId}", provider, "client_secret");
}
