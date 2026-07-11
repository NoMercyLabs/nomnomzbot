// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// <see cref="IKickApiClient"/> over api.kick.com/public/v1 (wire facts verified against live
/// docs.kick.com 2026-07-11). Same degradation contract as the YouTube twin: every transport/HTTP
/// failure becomes a typed <see cref="Result"/> (never throws), 401/403 map to <c>MISSING_SCOPE</c> so
/// an insufficient grant surfaces as a reauth need.
/// </summary>
public sealed class KickApiClient : IKickApiClient
{
    private const string KickApiBase = "https://api.kick.com/public/v1";
    private const int MessageCap = 500;
    private const int ReasonCap = 100;

    private readonly HttpClient _http;
    private readonly ILogger<KickApiClient> _logger;

    public KickApiClient(IHttpClientFactory httpClientFactory, ILogger<KickApiClient> logger)
    {
        _http = httpClientFactory.CreateClient("kick");
        _logger = logger;
    }

    public async Task<Result<string>> SendMessageAsync(
        string accessToken,
        long broadcasterUserId,
        string content,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default
    )
    {
        // Kick rejects >500-char messages — fail closed locally with the precise reason instead of
        // burning the call on a guaranteed 400.
        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure<string>("The message is empty.", "VALIDATION_FAILED");
        if (content.Length > MessageCap)
            return Result.Failure<string>(
                "Kick chat messages are capped at 500 characters.",
                "VALIDATION_FAILED"
            );

        HttpRequestMessage request = new(HttpMethod.Post, $"{KickApiBase}/chat");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(
            new
            {
                content,
                type = "user",
                broadcaster_user_id = broadcasterUserId,
                reply_to_message_id = replyToMessageId,
            }
        );

        Result<SendChatResponse> sent = await SendForBodyAsync<SendChatResponse>(
            request,
            $"send to channel {broadcasterUserId}",
            cancellationToken
        );
        if (sent.IsFailure)
            return Result.Failure<string>(sent.ErrorMessage!, sent.ErrorCode, sent.ErrorDetail);

        return Result.Success(sent.Value.Data?.MessageId ?? string.Empty);
    }

    public async Task<Result> DeleteMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default
    )
    {
        HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"{KickApiBase}/chat/{Uri.EscapeDataString(messageId)}"
        );
        request.Headers.Authorization = new("Bearer", accessToken);
        return await SendAsync(request, $"delete message {messageId}", cancellationToken);
    }

    public Task<Result> TimeoutUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        int durationMinutes,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        PostBanAsync(
            accessToken,
            new
            {
                broadcaster_user_id = broadcasterUserId,
                user_id = userId,
                duration = durationMinutes,
                reason = Truncate(reason),
            },
            $"timeout {userId} in {broadcasterUserId}",
            cancellationToken
        );

    public Task<Result> BanUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) =>
        PostBanAsync(
            accessToken,
            new
            {
                broadcaster_user_id = broadcasterUserId,
                user_id = userId,
                reason = Truncate(reason),
            },
            $"ban {userId} in {broadcasterUserId}",
            cancellationToken
        );

    public async Task<Result> UnbanUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        CancellationToken cancellationToken = default
    )
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"{KickApiBase}/moderation/bans");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(
            new { broadcaster_user_id = broadcasterUserId, user_id = userId }
        );
        return await SendAsync(
            request,
            $"unban {userId} in {broadcasterUserId}",
            cancellationToken
        );
    }

    private async Task<Result> PostBanAsync(
        string accessToken,
        object body,
        string operation,
        CancellationToken ct
    )
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"{KickApiBase}/moderation/bans");
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Content = JsonContent.Create(body);
        return await SendAsync(request, operation, ct);
    }

    /// <summary>Kick caps a moderation reason at 100 characters — truncate locally, never 400.</summary>
    private static string? Truncate(string? reason) =>
        reason is { Length: > ReasonCap } ? reason[..ReasonCap] : reason;

    /// <summary>Shared outcome mapping: 401/403 → MISSING_SCOPE, 404 → NOT_FOUND, other non-success →
    /// SERVICE_UNAVAILABLE; transport exceptions degrade the same way (never throw).</summary>
    private async Task<Result> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken
    )
    {
        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            return MapStatus(response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Kick API call threw for {Operation}", operation);
            return Result.Failure("Kick is temporarily unavailable.", "SERVICE_UNAVAILABLE");
        }
    }

    /// <summary>The <see cref="SendAsync"/> variant for calls whose response body the caller needs.</summary>
    private async Task<Result<TBody>> SendForBodyAsync<TBody>(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken
    )
        where TBody : class
    {
        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            Result mapped = MapStatus(response.StatusCode);
            if (mapped.IsFailure)
                return Result.Failure<TBody>(
                    mapped.ErrorMessage!,
                    mapped.ErrorCode,
                    mapped.ErrorDetail
                );

            TBody? body = await response.Content.ReadFromJsonAsync<TBody>(
                cancellationToken: cancellationToken
            );
            return body is null
                ? Result.Failure<TBody>("Kick is temporarily unavailable.", "SERVICE_UNAVAILABLE")
                : Result.Success(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Kick API call threw for {Operation}", operation);
            return Result.Failure<TBody>("Kick is temporarily unavailable.", "SERVICE_UNAVAILABLE");
        }
    }

    private static Result MapStatus(HttpStatusCode status)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return Result.Failure(
                "The Kick connection is missing the required scope.",
                "MISSING_SCOPE"
            );
        if (status == HttpStatusCode.NotFound)
            return Result.Failure("The Kick resource was not found.", "NOT_FOUND");
        return (int)status is >= 200 and < 300
            ? Result.Success()
            : Result.Failure("Kick is temporarily unavailable.", "SERVICE_UNAVAILABLE");
    }

    // ─── Wire models (Kick public v1) ─────────────────────────────────────────

    private sealed class SendChatResponse
    {
        [JsonPropertyName("data")]
        public SendChatData? Data { get; set; }
    }

    private sealed class SendChatData
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }
    }
}
