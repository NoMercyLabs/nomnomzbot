// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Automation.Entities;

namespace NomNomzBot.Infrastructure.AutomationApi;

/// <summary>
/// Resolves a presented data-plane secret to its <see cref="AutomationPrincipal"/> (automation-api.md §3):
/// SHA-256 hash lookup (cross-tenant by design — the secret IS the tenant selector, so the lookup ignores
/// the tenant filter), then fail-closed checks for revocation and expiry. <c>LastUsedAt</c> is stamped at
/// most once a minute so a chatty integration doesn't turn every call into a row write.
/// </summary>
public class AutomationTokenAuthenticator : IAutomationTokenAuthenticator
{
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _clock;

    public AutomationTokenAuthenticator(IApplicationDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<AutomationPrincipal>> AuthenticateAsync(
        string presentedSecret,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(presentedSecret))
            return Unauthorized();

        string hash = AutomationApiTokenService.HashSecret(presentedSecret);
        // IgnoreQueryFilters: authentication happens BEFORE any tenant is known — the token row itself
        // names the tenant. Soft-deleted rows are excluded explicitly, keeping the delete tombstone dead.
        AutomationApiToken? token = await _db
            .AutomationApiTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.DeletedAt == null, ct);
        if (token is null)
            return Unauthorized();

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (token.RevokedAt is not null)
            return Unauthorized();
        if (token.ExpiresAt is DateTime expiry && expiry <= now)
            return Unauthorized();

        if (token.LastUsedAt is null || now - token.LastUsedAt >= LastUsedWriteInterval)
        {
            token.LastUsedAt = now;
            await _db.SaveChangesAsync(ct);
        }

        return Result.Success(
            new AutomationPrincipal(
                token.BroadcasterId,
                token.Id,
                token.Name,
                JsonSerializer.Deserialize<List<string>>(token.ScopesJson) ?? [],
                token.AllowedPipelineIdsJson is null
                    ? null
                    : JsonSerializer.Deserialize<List<Guid>>(token.AllowedPipelineIdsJson)
            )
        );
    }

    /// <summary>One uniform rejection — why a credential failed is never disclosed to the caller.</summary>
    private static Result<AutomationPrincipal> Unauthorized() =>
        Result.Failure<AutomationPrincipal>("Invalid automation token.", "UNAUTHENTICATED");
}
