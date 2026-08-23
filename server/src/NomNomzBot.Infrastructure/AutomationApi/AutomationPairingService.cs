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
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
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

    /// <summary>A paired device token expires 30 days out (stream-deck.md D8) — unlike a human-managed
    /// dashboard-created token (no-expiry default via the management-plane CreateAsync), an unattended
    /// device credential should die if abandoned. The plugin self-refreshes via RefreshSelfAsync well
    /// before this, so a healthy device never re-pairs.</summary>
    private static readonly TimeSpan PairedTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>Device kind sent by the NomNomzBot Stream Deck plugin (streamdeck-plugin.md P6) — the
    /// only device kind that auto-provisions pipelines today, since it's the only automation client
    /// invoking by a fixed, well-known action-type-as-name convention.</summary>
    private const string StreamDeckDeviceKind = "streamdeck";

    /// <summary>Device-initiated flow (RFC 8628-shaped): longer than the dashboard-mint code's 5
    /// minutes since a human has to notice the prompt, open a browser, and log in — not just glance
    /// at a code already on screen.</summary>
    private static readonly TimeSpan DeviceCodeTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DevicePollInterval = TimeSpan.FromSeconds(3);

    private readonly ICacheService _cache;
    private readonly IAutomationApiTokenService _tokens;
    private readonly IRateLimiterPartitionStore _rateLimiter;
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<ICommandAction> _actions;
    private readonly TimeProvider _clock;

    public AutomationPairingService(
        ICacheService cache,
        IAutomationApiTokenService tokens,
        IRateLimiterPartitionStore rateLimiter,
        IApplicationDbContext db,
        IEnumerable<ICommandAction> actions,
        TimeProvider clock
    )
    {
        _cache = cache;
        _tokens = tokens;
        _rateLimiter = rateLimiter;
        _db = db;
        _actions = actions;
        _clock = clock;
    }

    /// <summary>The cached envelope a code resolves to — everything the redeem needs to mint the token.</summary>
    public sealed record PairingCodeEnvelope(
        Guid BroadcasterId,
        Guid ActorUserId,
        string DeviceLabel,
        IReadOnlyList<string> Scopes
    );

    /// <summary>
    /// The cached envelope a device-initiated pairing resolves to. Unapproved (<see cref="BroadcasterId"/>
    /// / <see cref="ActorUserId"/> both null) until an operator approves by <see cref="UserCode"/>.
    /// </summary>
    public sealed record DevicePairingEnvelope(
        string DeviceCode,
        string UserCode,
        string BackendUrl,
        DeviceInfo Device,
        IReadOnlyList<string> Scopes,
        Guid? BroadcasterId,
        Guid? ActorUserId
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

        Result<IssuedAutomationTokenDto> issued = await MintDeviceTokenAsync(
            envelope.BroadcasterId,
            envelope.ActorUserId,
            device,
            envelope.Scopes,
            normalized[^4..],
            ct
        );
        if (issued.IsFailure)
            return Result.Failure<PairingRedemptionDto>(
                issued.ErrorMessage!,
                issued.ErrorCode!,
                issued.ErrorDetail
            );

        DateTime expiresAt = _clock.GetUtcNow().UtcDateTime.Add(PairedTokenLifetime);
        return Result.Success(
            new PairingRedemptionDto(backendUrl, issued.Value.Secret, envelope.Scopes, expiresAt)
        );
    }

    public async Task<Result<DeviceInitDto>> InitDeviceAsync(
        DeviceInfo device,
        string backendUrl,
        IReadOnlyList<string>? scopes,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<string> resolvedScopes = scopes is { Count: > 0 } ? scopes : DefaultScopes;
        foreach (string scope in resolvedScopes)
            if (!KnownScopes.Contains(scope))
                return Errors
                    .ValidationFailed(
                        $"Unknown scope '{scope}' — valid scopes: {string.Join(", ", KnownScopes)}."
                    )
                    .ToTyped<DeviceInitDto>();

        string deviceCode = Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string userCode = MintCode();

        DevicePairingEnvelope envelope = new(
            deviceCode,
            userCode,
            backendUrl,
            device,
            [.. resolvedScopes.Distinct()],
            BroadcasterId: null,
            ActorUserId: null
        );
        await _cache.SetAsync($"devpair:{deviceCode}", envelope, DeviceCodeTtl, ct);
        await _cache.SetAsync($"devpair:code:{userCode}", deviceCode, DeviceCodeTtl, ct);

        DateTime expiresAt = _clock.GetUtcNow().UtcDateTime.Add(DeviceCodeTtl);
        return Result.Success(
            new DeviceInitDto(
                deviceCode,
                userCode,
                $"{backendUrl}/api/v1/automation/pair/device/approve?code={userCode}",
                expiresAt,
                (int)DevicePollInterval.TotalSeconds
            )
        );
    }

    public async Task<Result> ApproveDeviceAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string userCode,
        CancellationToken ct = default
    )
    {
        // Authenticated, but the code is still an 8-char human alphabet another user's device chose —
        // guard against a logged-in caller guessing at someone else's pending pairing.
        RateLimitLease lease = await _rateLimiter.AcquireAsync(
            $"automation:approve:{actorUserId}",
            RedeemsPerClientPerMinute,
            GuardWindow,
            ct
        );
        if (!lease.IsAcquired)
        {
            int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(lease.RetryAfter.TotalSeconds));
            return Result.Failure(
                $"Too many attempts — retry in {retryAfterSeconds}s.",
                "RATE_LIMITED",
                retryAfterSeconds.ToString()
            );
        }

        string normalized = userCode.Trim().ToUpperInvariant();
        string? deviceCode = await _cache.GetAsync<string>($"devpair:code:{normalized}", ct);
        if (deviceCode is null)
            return Result.Failure("Invalid or expired pairing code.", "UNAUTHENTICATED");

        DevicePairingEnvelope? envelope = await _cache.GetAsync<DevicePairingEnvelope>(
            $"devpair:{deviceCode}",
            ct
        );
        if (envelope is null)
            return Result.Failure("Invalid or expired pairing code.", "UNAUTHENTICATED");

        // Single-use: the userCode index is gone the moment it's approved, so a second approve attempt
        // (or a raced concurrent one) can never rebind an already-approved device to a different caller.
        await _cache.RemoveAsync($"devpair:code:{normalized}", ct);

        DevicePairingEnvelope approved = envelope with
        {
            BroadcasterId = broadcasterId,
            ActorUserId = actorUserId,
        };
        await _cache.SetAsync($"devpair:{deviceCode}", approved, DeviceCodeTtl, ct);
        return Result.Success();
    }

    public async Task<Result<DevicePollDto>> PollDeviceAsync(
        string deviceCode,
        CancellationToken ct = default
    )
    {
        DevicePairingEnvelope? envelope = await _cache.GetAsync<DevicePairingEnvelope>(
            $"devpair:{deviceCode}",
            ct
        );
        if (envelope is null)
            return Result.Failure<DevicePollDto>(
                "Unknown, expired, or already-claimed device code.",
                "NOT_FOUND"
            );
        if (
            envelope.BroadcasterId is not { } broadcasterId
            || envelope.ActorUserId is not { } actorUserId
        )
            return Result.Success(new DevicePollDto("pending"));

        // Consume BEFORE minting: a raced second poll after approval must never yield a second secret.
        await _cache.RemoveAsync($"devpair:{deviceCode}", ct);

        Result<IssuedAutomationTokenDto> issued = await MintDeviceTokenAsync(
            broadcasterId,
            actorUserId,
            envelope.Device,
            envelope.Scopes,
            envelope.UserCode[^4..],
            ct
        );
        if (issued.IsFailure)
            return Result.Failure<DevicePollDto>(
                issued.ErrorMessage!,
                issued.ErrorCode!,
                issued.ErrorDetail
            );

        DateTime expiresAt = _clock.GetUtcNow().UtcDateTime.Add(PairedTokenLifetime);
        return Result.Success(
            new DevicePollDto(
                "approved",
                envelope.BackendUrl,
                issued.Value.Secret,
                envelope.Scopes,
                expiresAt
            )
        );
    }

    /// <summary>
    /// Shared mint step for both pairing flows: names the token after the device, retries once with a
    /// short disambiguator on a name collision (the same device re-pairing under the same label), and
    /// auto-provisions the Stream Deck music pipelines on success. Does NOT compute the token's
    /// expiry — callers stamp that themselves since the two flows read it back slightly differently.
    /// </summary>
    private async Task<Result<IssuedAutomationTokenDto>> MintDeviceTokenAsync(
        Guid broadcasterId,
        Guid actorUserId,
        DeviceInfo device,
        IReadOnlyList<string> scopes,
        string disambiguator,
        CancellationToken ct
    )
    {
        string deviceName = string.IsNullOrWhiteSpace(device.Name)
            ? device.Kind.Trim()
            : device.Name.Trim();
        string tokenName = $"{device.Kind.Trim()}: {deviceName}";
        DateTime expiresAt = _clock.GetUtcNow().UtcDateTime.Add(PairedTokenLifetime);

        Result<IssuedAutomationTokenDto> issued = await _tokens.CreateAsync(
            broadcasterId,
            actorUserId,
            new()
            {
                Name = tokenName,
                Scopes = scopes,
                ExpiresAt = expiresAt,
            },
            ct
        );
        if (issued is { IsFailure: true, ErrorCode: "ALREADY_EXISTS" })
        {
            // Same device paired again under the same label — disambiguate rather than failing a
            // pairing the operator deliberately approved.
            issued = await _tokens.CreateAsync(
                broadcasterId,
                actorUserId,
                new()
                {
                    Name = $"{tokenName} ({disambiguator})",
                    Scopes = scopes,
                    ExpiresAt = expiresAt,
                },
                ct
            );
        }
        if (issued.IsSuccess && IsStreamDeck(device.Kind))
            await EnsureMusicActionPipelinesAsync(broadcasterId, ct);

        return issued;
    }

    private static bool IsStreamDeck(string deviceKind) =>
        string.Equals(deviceKind.Trim(), StreamDeckDeviceKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Auto-provisions D7's remaining gap: one single-step pipeline per registered <c>music_*</c> action,
    /// named identically to its <see cref="ICommandAction.ActionType"/>. The Stream Deck plugin invokes
    /// pipelines BY NAME (automation-api.md §3 <c>AutomationInvokeRequest.PipelineName</c>) assuming a
    /// pipeline matching each of its 22 manifest actions already exists — this makes that true on first
    /// pairing, with zero manual dashboard setup. Derives the action list from the live
    /// <see cref="ICommandAction"/> registry rather than a hardcoded string list, so a future <c>music_*</c>
    /// action is auto-provisioned too. Idempotent: skips any name that's already taken.
    /// </summary>
    private async Task EnsureMusicActionPipelinesAsync(Guid broadcasterId, CancellationToken ct)
    {
        string[] musicActionTypes =
        [
            .. _actions
                .Select(a => a.ActionType)
                .Where(t => t.StartsWith("music_", StringComparison.Ordinal))
                .Distinct(),
        ];
        if (musicActionTypes.Length == 0)
            return;

        HashSet<string> existingNames = (
            await _db
                .Pipelines.Where(p =>
                    p.BroadcasterId == broadcasterId && musicActionTypes.Contains(p.Name)
                )
                .Select(p => p.Name)
                .ToListAsync(ct)
        ).ToHashSet(StringComparer.Ordinal);

        foreach (string actionType in musicActionTypes)
        {
            if (existingNames.Contains(actionType))
                continue;

            _db.Pipelines.Add(
                new()
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = broadcasterId,
                    Name = actionType,
                    Description = "Auto-provisioned for the NomNomzBot Stream Deck plugin.",
                    TriggerKind = "manual",
                    IsEnabled = true,
                    GraphJsonCache = JsonSerializer.Serialize(
                        new PipelineDefinition
                        {
                            Steps = [new() { Action = new() { Type = actionType } }],
                        }
                    ),
                }
            );
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string MintCode()
    {
        char[] chars = new char[CodeLength];
        byte[] bytes = RandomNumberGenerator.GetBytes(CodeLength);
        for (int i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        return new(chars);
    }
}
