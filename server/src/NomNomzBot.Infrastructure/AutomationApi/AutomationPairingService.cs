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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.AutomationApi;

/// <summary>
/// Device pairing (stream-deck.md §3/D2/D6). Codes are 8 chars from an unambiguous alphabet, live in
/// the cache under <c>pair:{code}</c> for 5 minutes, and are consumed BEFORE the token mints so a
/// replay can never yield a second credential. The minted credential is a plain automation token
/// named after the device — it appears in the normal token list and revoke = unpair (D3). Redeems are
/// brute-force-guarded per caller AND globally; a denied or failed attempt mints nothing. The default
/// scope grant is <c>invoke</c>+<c>events</c>+<c>read</c> — <c>chat</c> only when the OPERATOR asked
/// for it at mint time; the device never chooses its own scopes.
/// </summary>
public class AutomationPairingService : IAutomationPairingService
{
    /// <summary>No 0/O/1/I/L — operators read these aloud or type them from a small screen.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(5);
    private static readonly string[] DefaultScopes = ["invoke", "events", "read"];
    private static readonly string[] KnownScopes = ["invoke", "read", "events", "chat"];

    private const int RedeemsPerClientPerMinute = 5;
    private const int RedeemsGlobalPerMinute = 30;
    private static readonly TimeSpan GuardWindow = TimeSpan.FromMinutes(1);

    private readonly ICacheService _cache;
    private readonly IAutomationApiTokenService _tokens;
    private readonly IRateLimiterPartitionStore _rateLimiter;
    private readonly TimeProvider _clock;

    public AutomationPairingService(
        ICacheService cache,
        IAutomationApiTokenService tokens,
        IRateLimiterPartitionStore rateLimiter,
        TimeProvider clock
    )
    {
        _cache = cache;
        _tokens = tokens;
        _rateLimiter = rateLimiter;
        _clock = clock;
    }

    /// <summary>The cached envelope a code resolves to — everything the redeem needs to mint the token.</summary>
    public sealed record PairingCodeEnvelope(
        Guid BroadcasterId,
        Guid ActorUserId,
        string DeviceLabel,
        IReadOnlyList<string> Scopes
    );

    public async Task<Result<PairingCodeDto>> MintCodeAsync(
        Guid broadcasterId,
        Guid actorUserId,
        MintPairingCodeRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.DeviceLabel))
            return Errors.ValidationFailed("A device label is required.").ToTyped<PairingCodeDto>();

        IReadOnlyList<string> scopes = request.Scopes is { Count: > 0 }
            ? request.Scopes
            : DefaultScopes;
        foreach (string scope in scopes)
            if (!KnownScopes.Contains(scope))
                return Errors
                    .ValidationFailed(
                        $"Unknown scope '{scope}' — valid scopes: {string.Join(", ", KnownScopes)}."
                    )
                    .ToTyped<PairingCodeDto>();

        string code = MintCode();
        PairingCodeEnvelope envelope = new(
            broadcasterId,
            actorUserId,
            request.DeviceLabel.Trim(),
            [.. scopes.Distinct()]
        );
        await _cache.SetAsync($"pair:{code}", envelope, CodeTtl, ct);

        DateTime expiresAt = _clock.GetUtcNow().UtcDateTime.Add(CodeTtl);
        return Result.Success(new PairingCodeDto(code, expiresAt));
    }

    public async Task<Result<PairingRedemptionDto>> RedeemCodeAsync(
        string code,
        DeviceInfo device,
        string clientKey,
        string backendUrl,
        CancellationToken ct = default
    )
    {
        // Brute-force guard FIRST (per caller + global), so guessing burns budget, never codes.
        RateLimitLease clientLease = await _rateLimiter.AcquireAsync(
            $"automation:pair:{clientKey}",
            RedeemsPerClientPerMinute,
            GuardWindow,
            ct
        );
        RateLimitLease globalLease = clientLease.IsAcquired
            ? await _rateLimiter.AcquireAsync(
                "automation:pair:global",
                RedeemsGlobalPerMinute,
                GuardWindow,
                ct
            )
            : clientLease;
        if (!clientLease.IsAcquired || !globalLease.IsAcquired)
        {
            TimeSpan retryAfter = clientLease.IsAcquired
                ? globalLease.RetryAfter
                : clientLease.RetryAfter;
            int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            return Result.Failure<PairingRedemptionDto>(
                $"Too many pairing attempts — retry in {retryAfterSeconds}s.",
                "RATE_LIMITED",
                retryAfterSeconds.ToString()
            );
        }

        string normalized = code.Trim().ToUpperInvariant();
        PairingCodeEnvelope? envelope = await _cache.GetAsync<PairingCodeEnvelope>(
            $"pair:{normalized}",
            ct
        );
        if (envelope is null)
            return Result.Failure<PairingRedemptionDto>(
                "Invalid or expired pairing code.",
                "UNAUTHENTICATED"
            );

        // Consume BEFORE minting: a raced second redeem must fail, never receive a second secret.
        await _cache.RemoveAsync($"pair:{normalized}", ct);

        string deviceName = string.IsNullOrWhiteSpace(device.Name)
            ? envelope.DeviceLabel
            : device.Name.Trim();
        string tokenName = $"{device.Kind.Trim()}: {deviceName}";
        Result<IssuedAutomationTokenDto> issued = await _tokens.CreateAsync(
            envelope.BroadcasterId,
            envelope.ActorUserId,
            new CreateAutomationTokenRequest { Name = tokenName, Scopes = envelope.Scopes },
            ct
        );
        if (issued is { IsFailure: true, ErrorCode: "ALREADY_EXISTS" })
        {
            // Same device paired again under the same label — disambiguate with the code's tail
            // rather than failing a pairing the operator deliberately initiated.
            issued = await _tokens.CreateAsync(
                envelope.BroadcasterId,
                envelope.ActorUserId,
                new CreateAutomationTokenRequest
                {
                    Name = $"{tokenName} ({normalized[^4..]})",
                    Scopes = envelope.Scopes,
                },
                ct
            );
        }
        if (issued.IsFailure)
            return Result.Failure<PairingRedemptionDto>(
                issued.ErrorMessage!,
                issued.ErrorCode!,
                issued.ErrorDetail
            );

        return Result.Success(
            new PairingRedemptionDto(backendUrl, issued.Value.Secret, envelope.Scopes)
        );
    }

    private static string MintCode()
    {
        char[] chars = new char[CodeLength];
        byte[] bytes = RandomNumberGenerator.GetBytes(CodeLength);
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new string(chars);
    }
}
