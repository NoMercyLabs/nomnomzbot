// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Domain.Automation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.AutomationApi;

/// <summary>
/// Automation token management (automation-api.md §3). The secret is minted from 32 CSPRNG bytes with
/// the <c>nnzb_ak_</c> prefix, returned exactly once, and persisted only as its SHA-256 hex — the same
/// discipline the refresh-token store uses. Every issue/rotate/revoke publishes a credential-audit
/// event (journaled, never streamed). Revocation tombstones the row; rotation refuses a revoked or
/// expired credential rather than resurrecting it.
/// </summary>
public class AutomationApiTokenService : IAutomationApiTokenService
{
    private const string SecretPrefix = "nnzb_ak_";
    private static readonly string[] KnownScopes = ["invoke", "read", "events", "chat"];

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly IAutomationEventRegistry _eventRegistry;

    public AutomationApiTokenService(
        IApplicationDbContext db,
        IEventBus eventBus,
        TimeProvider clock,
        IAutomationEventRegistry eventRegistry
    )
    {
        _db = db;
        _eventBus = eventBus;
        _clock = clock;
        _eventRegistry = eventRegistry;
    }

    public Task<Result<IReadOnlyList<AutomationEventCatalogItem>>> GetEventCatalogAsync(
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success(_eventRegistry.Catalog));

    public async Task<Result<PagedList<AutomationTokenDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<AutomationApiToken> query = _db.AutomationApiTokens.Where(t =>
            t.BroadcasterId == broadcasterId
        );
        int total = await query.CountAsync(ct);
        List<AutomationApiToken> rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<AutomationTokenDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<IssuedAutomationTokenDto>> CreateAsync(
        Guid broadcasterId,
        Guid createdByUserId,
        CreateAutomationTokenRequest request,
        CancellationToken ct = default
    )
    {
        Result scopesValid = ValidateScopes(request.Scopes);
        if (scopesValid.IsFailure)
            return Result.Failure<IssuedAutomationTokenDto>(
                scopesValid.ErrorMessage!,
                scopesValid.ErrorCode!
            );

        // IgnoreQueryFilters: the DB unique index (BroadcasterId, Name) sees soft-deleted rows too —
        // an app-side check that missed one would surface as a 500 on insert instead of ALREADY_EXISTS.
        bool nameTaken = await _db
            .AutomationApiTokens.IgnoreQueryFilters()
            .AnyAsync(t => t.BroadcasterId == broadcasterId && t.Name == request.Name, ct);
        if (nameTaken)
            return Result.Failure<IssuedAutomationTokenDto>(
                $"A token named '{request.Name}' already exists.",
                "ALREADY_EXISTS"
            );

        (string secret, string hash, string prefix) = MintSecret();
        AutomationApiToken token = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name,
            TokenHash = hash,
            TokenPrefix = prefix,
            ScopesJson = JsonSerializer.Serialize(Normalize(request.Scopes)),
            AllowedPipelineIdsJson = request.AllowedPipelineIds is { Count: > 0 }
                ? JsonSerializer.Serialize(request.AllowedPipelineIds)
                : null,
            ExpiresAt = request.ExpiresAt,
            CreatedByUserId = createdByUserId,
        };
        _db.AutomationApiTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        await PublishIssuedAsync(token, createdByUserId, wasRotation: false, ct);
        return Result.Success(new IssuedAutomationTokenDto(secret, ToDto(token)));
    }

    public async Task<Result<IssuedAutomationTokenDto>> RotateAsync(
        Guid broadcasterId,
        Guid tokenId,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        AutomationApiToken? token = await _db.AutomationApiTokens.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.Id == tokenId,
            ct
        );
        if (token is null)
            return Errors.NotFound<IssuedAutomationTokenDto>(
                "Automation token",
                tokenId.ToString()
            );
        if (token.RevokedAt is not null)
            return Errors
                .ValidationFailed("A revoked token cannot be rotated — create a new one.")
                .ToTyped<IssuedAutomationTokenDto>();

        (string secret, string hash, string prefix) = MintSecret();
        token.TokenHash = hash;
        token.TokenPrefix = prefix;
        await _db.SaveChangesAsync(ct);

        await PublishIssuedAsync(token, actorUserId, wasRotation: true, ct);
        return Result.Success(new IssuedAutomationTokenDto(secret, ToDto(token)));
    }

    public async Task<Result<bool>> RevokeAsync(
        Guid broadcasterId,
        Guid tokenId,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        AutomationApiToken? token = await _db.AutomationApiTokens.FirstOrDefaultAsync(
            t => t.BroadcasterId == broadcasterId && t.Id == tokenId,
            ct
        );
        if (token is null)
            return Errors.NotFound<bool>("Automation token", tokenId.ToString());
        if (token.RevokedAt is not null)
            return Result.Success(true); // idempotent — the tombstone (and its audit event) already exist

        token.RevokedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new AutomationTokenRevokedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = _clock.GetUtcNow(),
                TokenId = token.Id,
                RevokedByUserId = actorUserId,
            },
            ct
        );
        return Result.Success(true);
    }

    private async Task PublishIssuedAsync(
        AutomationApiToken token,
        Guid actorUserId,
        bool wasRotation,
        CancellationToken ct
    ) =>
        await _eventBus.PublishAsync(
            new AutomationTokenIssuedEvent
            {
                BroadcasterId = token.BroadcasterId,
                OccurredAt = _clock.GetUtcNow(),
                TokenId = token.Id,
                TokenName = token.Name,
                Scopes = DeserializeScopes(token.ScopesJson),
                CreatedByUserId = actorUserId,
                WasRotation = wasRotation,
            },
            ct
        );

    private static Result ValidateScopes(IReadOnlyList<string> scopes)
    {
        if (scopes.Count == 0)
            return Errors.ValidationFailed("At least one scope is required.");
        foreach (string scope in scopes)
            if (!KnownScopes.Contains(scope))
                return Errors.ValidationFailed(
                    $"Unknown scope '{scope}' — valid scopes: {string.Join(", ", KnownScopes)}."
                );
        return Result.Success();
    }

    private static List<string> Normalize(IReadOnlyList<string> scopes) =>
        [.. scopes.Distinct().OrderBy(s => Array.IndexOf(KnownScopes, s))];

    /// <summary>32 CSPRNG bytes behind the <c>nnzb_ak_</c> marker; the prefix is the display handle.</summary>
    private static (string Secret, string Hash, string Prefix) MintSecret()
    {
        string secret = SecretPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        string hash = HashSecret(secret);
        string prefix = secret[..(SecretPrefix.Length + 4)];
        return (secret, hash, prefix);
    }

    /// <summary>The stored (and lookup) form of a presented secret — SHA-256 lowercase hex.</summary>
    public static string HashSecret(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static IReadOnlyList<string> DeserializeScopes(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static AutomationTokenDto ToDto(AutomationApiToken t) =>
        new(
            t.Id,
            t.Name,
            t.TokenPrefix,
            DeserializeScopes(t.ScopesJson),
            t.AllowedPipelineIdsJson is null
                ? []
                : JsonSerializer.Deserialize<List<Guid>>(t.AllowedPipelineIdsJson) ?? [],
            t.LastUsedAt,
            t.ExpiresAt,
            t.RevokedAt,
            t.CreatedAt
        );
}
