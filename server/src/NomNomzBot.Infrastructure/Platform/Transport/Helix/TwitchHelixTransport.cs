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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// The DTO-agnostic Helix send pipeline (twitch-helix.md §3, §7) every per-endpoint method rides on. It owns
/// the cross-cutting transport concerns so the hand-written sub-client methods stay thin:
/// <list type="bullet">
///   <item>resolves the per-call bearer (app/bot vs broadcaster user token) and stows it + the bucket key on
///   the request options for the delegating handlers;</item>
///   <item>builds the <c>https://api.twitch.tv/helix/{path}?{query}</c> URL with URL-encoded query values;</item>
///   <item>serialises the body and deserialises the <c>data[]</c> / <c>pagination</c> / <c>total</c> envelope
///   (System.Text.Json, snake_case wire);</item>
///   <item>handles the 401 → single refresh-and-retry, then maps the outcome to a typed <see cref="Result"/>.</item>
/// </list>
/// Adaptive rate limiting (header-driven) and resilience (retry+breaker, no-retry on 4xx) are layered as
/// delegating handlers on the named <c>twitch-helix</c> client — this class never re-implements them.
/// </summary>
public sealed class TwitchHelixTransport(
    IHttpClientFactory httpClientFactory,
    ITwitchTokenResolver tokenResolver,
    ISystemCredentialsProvider credentials,
    IEventBus eventBus,
    ILogger<TwitchHelixTransport> logger
) : ITwitchHelixTransport
{
    private const string HelixBase = "https://api.twitch.tv/helix";

    // Twitch wire JSON is snake_case end to end (read and write). The naming policy lets the
    // per-endpoint DTOs stay plain PascalCase records with no per-property annotations, and
    // WhenWritingNull keeps PATCH bodies to only the fields the caller actually set.
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = httpClientFactory.CreateClient("twitch-helix");

    public async Task<Result<TResponse>> GetSingleAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Result<HelixEnvelope<TResponse>> result = await SendEnvelopeAsync<TResponse>(request, ct);
        if (result.IsFailure)
            return result.WithValue<TResponse>(default!);

        TResponse? first = (result.Value.Data ?? []).FirstOrDefault();
        return first is null
            ? Result.Failure<TResponse>("Twitch returned no data.", TwitchErrorCodes.NotFound)
            : Result.Success(first);
    }

    public async Task<Result<IReadOnlyList<TResponse>>> GetListAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Result<HelixEnvelope<TResponse>> result = await SendEnvelopeAsync<TResponse>(request, ct);
        if (result.IsFailure)
            return result.WithValue<IReadOnlyList<TResponse>>(default!);

        return Result.Success<IReadOnlyList<TResponse>>(result.Value.Data ?? []);
    }

    public async Task<Result<TwitchPage<TResponse>>> GetPageAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Result<HelixEnvelope<TResponse>> result = await SendEnvelopeAsync<TResponse>(request, ct);
        if (result.IsFailure)
            return result.WithValue<TwitchPage<TResponse>>(default!);

        HelixEnvelope<TResponse> envelope = result.Value;
        return Result.Success(
            new TwitchPage<TResponse>(
                envelope.Data ?? [],
                envelope.Pagination?.Cursor,
                envelope.Total ?? 0
            )
        );
    }

    public async Task<Result<int>> GetTotalAsync(
        TwitchHelixRequest request,
        CancellationToken ct = default
    )
    {
        Result<HelixEnvelope<JsonElement>> result = await SendEnvelopeAsync<JsonElement>(
            request,
            ct
        );
        return result.IsFailure ? result.WithValue(0) : Result.Success(result.Value.Total ?? 0);
    }

    public async Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default)
    {
        Result<HttpResponseMessage> sent = await SendCoreAsync(request, ct);
        if (sent.IsFailure)
            return sent;

        using HttpResponseMessage response = sent.Value;
        return response.IsSuccessStatusCode
            ? Result.Success()
            : await MapErrorAsync(request, response, ct);
    }

    public Task<Result<TResponse>> SendWithResultAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct = default
    ) => GetSingleAsync<TResponse>(request, ct);

    private async Task<Result<HelixEnvelope<TResponse>>> SendEnvelopeAsync<TResponse>(
        TwitchHelixRequest request,
        CancellationToken ct
    )
    {
        Result<HttpResponseMessage> sent = await SendCoreAsync(request, ct);
        if (sent.IsFailure)
            return sent.WithValue<HelixEnvelope<TResponse>>(default!);

        using HttpResponseMessage response = sent.Value;
        if (!response.IsSuccessStatusCode)
        {
            Result error = await MapErrorAsync(request, response, ct);
            return error.WithValue<HelixEnvelope<TResponse>>(default!);
        }

        try
        {
            HelixEnvelope<TResponse>? envelope = await response.Content.ReadFromJsonAsync<
                HelixEnvelope<TResponse>
            >(WireJson, ct);
            return Result.Success(envelope ?? new HelixEnvelope<TResponse>());
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize Helix response for {Path}", request.Path);
            return Result.Failure<HelixEnvelope<TResponse>>(
                "Malformed Twitch response.",
                TwitchErrorCodes.Transport
            );
        }
    }

    /// <summary>
    /// Resolves the bearer, builds + sends the request, and performs the single 401 refresh-and-retry.
    /// Returns the raw response for the caller to interpret; a failed token resolution or a transport
    /// exception short-circuits to a typed failure.
    /// </summary>
    private async Task<Result<HttpResponseMessage>> SendCoreAsync(
        TwitchHelixRequest request,
        CancellationToken ct
    )
    {
        Result<TwitchAccessContext> tokenResult =
            request.Auth == TwitchHelixAuth.User && request.BroadcasterId is { } tenant
                ? await tokenResolver.GetBroadcasterTokenAsync(tenant, ct)
                : await tokenResolver.GetBotTokenAsync(ct);

        if (tokenResult.IsFailure)
            return tokenResult.WithValue<HttpResponseMessage>(default!);

        TwitchAccessContext context = tokenResult.Value;
        string url = BuildUrl(request);

        // Resolve the app Client-Id the same DB-vaulted-first way the OAuth flows do, so a wizard-configured
        // (config-less) deployment still sends the right Client-Id header. Null only on a wholly unconfigured
        // system, in which case the token resolution above would already have failed.
        string? clientId = await credentials.GetValueAsync(
            AuthEnums.IntegrationProvider.Twitch,
            "client_id",
            ct
        );

        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using HttpRequestMessage message = BuildMessage(request, url, context, clientId);
                response = await _http.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Helix transport failed for {Path}", request.Path);
                return Result.Failure<HttpResponseMessage>(
                    "Twitch request failed.",
                    TwitchErrorCodes.Transport
                );
            }

            // 401 on the first attempt of a user-token call ⇒ refresh once and retry.
            if (
                response.StatusCode == HttpStatusCode.Unauthorized
                && attempt == 0
                && context.BroadcasterId is not null
            )
            {
                response.Dispose();
                Result<TwitchAccessContext> refreshed = await tokenResolver.RefreshAsync(
                    context,
                    ct
                );
                if (refreshed.IsFailure)
                {
                    await PublishReauthAsync(context, "unauthorized", null, ct);
                    return Result.Failure<HttpResponseMessage>(
                        "Twitch rejected the token.",
                        TwitchErrorCodes.Unauthorized
                    );
                }

                context = refreshed.Value;
                continue;
            }

            return Result.Success(response);
        }

        // Both attempts returned 401 — the refreshed token was still rejected.
        await PublishReauthAsync(context, "unauthorized", null, ct);
        return Result.Failure<HttpResponseMessage>(
            "Twitch rejected the token after refresh.",
            TwitchErrorCodes.Unauthorized
        );
    }

    private static HttpRequestMessage BuildMessage(
        TwitchHelixRequest request,
        string url,
        TwitchAccessContext context,
        string? clientId
    )
    {
        HttpRequestMessage message = new(request.Method, url);
        message.Options.Set(HelixRequestOptions.AccessToken, context.AccessToken);
        message.Options.Set(HelixRequestOptions.TokenBucketKey, context.TokenBucketKey);
        message.Options.Set(HelixRequestOptions.BroadcasterId, context.BroadcasterId);
        if (!string.IsNullOrEmpty(clientId))
            message.Options.Set(HelixRequestOptions.ClientId, clientId);

        if (request.Body is not null)
            message.Content = JsonContent.Create(request.Body, options: WireJson);

        return message;
    }

    private static string BuildUrl(TwitchHelixRequest request)
    {
        StringBuilder url = new($"{HelixBase}/{request.Path}");
        if (request.Query is { Count: > 0 })
        {
            url.Append('?');
            bool first = true;
            foreach (KeyValuePair<string, string> pair in request.Query)
            {
                if (!first)
                    url.Append('&');
                url.Append(Uri.EscapeDataString(pair.Key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(pair.Value));
                first = false;
            }
        }

        return url.ToString();
    }

    /// <summary>Maps a non-success Helix status to the closed error-code set (twitch-helix.md §3).</summary>
    private async Task<Result> MapErrorAsync(
        TwitchHelixRequest request,
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        string code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => TwitchErrorCodes.Unauthorized,
            HttpStatusCode.NotFound => TwitchErrorCodes.NotFound,
            HttpStatusCode.TooManyRequests => TwitchErrorCodes.RateLimited,
            _ => TwitchErrorCodes.TwitchError,
        };

        string? detail = await SafeReadBodyAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            await PublishReauthAsync(request, "unauthorized", null, ct);

        logger.LogWarning(
            "Helix {Method} {Path} failed: {Status} ({Code})",
            request.Method,
            request.Path,
            (int)response.StatusCode,
            code
        );

        return Result.Failure($"Twitch request failed ({(int)response.StatusCode}).", code, detail);
    }

    private static async Task<string?> SafeReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            HelixErrorBody? body = await response.Content.ReadFromJsonAsync<HelixErrorBody>(
                WireJson,
                ct
            );
            return body?.Message ?? body?.Error;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException)
        {
            return null;
        }
    }

    private Task PublishReauthAsync(
        TwitchAccessContext context,
        string reason,
        string? missingScope,
        CancellationToken ct
    ) =>
        PublishReauthCoreAsync(
            context.BroadcasterId,
            context.ServiceName,
            reason,
            missingScope,
            ct
        );

    private Task PublishReauthAsync(
        TwitchHelixRequest request,
        string reason,
        string? missingScope,
        CancellationToken ct
    ) =>
        PublishReauthCoreAsync(
            request.BroadcasterId,
            request.Auth == TwitchHelixAuth.User ? "twitch" : "twitch_bot",
            reason,
            missingScope,
            ct
        );

    private Task PublishReauthCoreAsync(
        Guid? broadcasterId,
        string serviceName,
        string reason,
        string? missingScope,
        CancellationToken ct
    ) =>
        eventBus.PublishAsync(
            new TwitchHelixReauthRequiredEvent
            {
                BroadcasterId = broadcasterId ?? Guid.Empty,
                Provider = "twitch",
                ServiceName = serviceName,
                Reason = reason,
                MissingScope = missingScope,
            },
            ct
        );
}
