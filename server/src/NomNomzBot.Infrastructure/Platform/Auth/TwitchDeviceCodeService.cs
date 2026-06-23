// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Twitch <em>Device Code Flow</em> HTTP (<see cref="ITwitchDeviceCodeService"/>): request a device/user code,
/// then poll the token endpoint until the operator approves. Reads only the client id — NomNomzBot's shipped
/// public id or a BYOC override (<see cref="ISystemCredentialsProvider.GetClientIdAsync"/>) — and sends no
/// secret. Pure HTTP: the caller vaults the issued tokens via <see cref="IIntegrationTokenVault"/> (mirrors
/// <see cref="TwitchAuthService.ExchangeCodeAsync"/>). Shares the <c>"twitch-auth"</c> named client.
/// </summary>
public sealed class TwitchDeviceCodeService : ITwitchDeviceCodeService
{
    private readonly ISystemCredentialsProvider _credentials;
    private readonly DeviceCodePollThrottle _throttle;
    private readonly HttpClient _http;
    private readonly ILogger<TwitchDeviceCodeService> _logger;
    private readonly TimeProvider _timeProvider;

    private const string DeviceEndpoint = "https://id.twitch.tv/oauth2/device";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";
    private const string TwitchProvider = AuthEnums.IntegrationProvider.Twitch;

    // Twitch's device-flow poll interval is 5s; never forward a poll to Twitch faster than this per code.
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(5);

    public TwitchDeviceCodeService(
        ISystemCredentialsProvider credentials,
        DeviceCodePollThrottle throttle,
        IHttpClientFactory httpClientFactory,
        ILogger<TwitchDeviceCodeService> logger,
        TimeProvider timeProvider
    )
    {
        _credentials = credentials;
        _throttle = throttle;
        _http = httpClientFactory.CreateClient("twitch-auth");
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceCodeResult?> RequestDeviceCodeAsync(
        IReadOnlyList<string> scopes,
        CancellationToken ct = default
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, ct);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("Device authorization skipped: no Twitch client id is configured.");
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', scopes),
            }
        );

        HttpResponseMessage resp = await _http.PostAsync(DeviceEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Device authorization request failed: {Status}", resp.StatusCode);
            return null;
        }

        TwitchDeviceCodeResponse? json =
            await resp.Content.ReadFromJsonAsync<TwitchDeviceCodeResponse>(cancellationToken: ct);
        if (json is null)
            return null;

        return new DeviceCodeResult(
            json.DeviceCode,
            json.UserCode,
            json.VerificationUri,
            json.Interval,
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn)
        );
    }

    public async Task<DevicePollOutcome> PollOnceAsync(
        string deviceCode,
        IReadOnlyList<string> scopes,
        CancellationToken ct = default
    )
    {
        // Etiquette guard: never forward a poll to Twitch faster than the device-flow interval for this code,
        // however fast — or however many — clients call us; Twitch returns slow_down otherwise. A too-soon poll
        // is reported as still pending, with no Twitch round-trip.
        if (!_throttle.TryAcquire(deviceCode, MinPollInterval))
            return new DevicePollOutcome(DevicePollStatus.Pending);

        string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, ct);
        if (string.IsNullOrWhiteSpace(clientId))
            return new DevicePollOutcome(DevicePollStatus.Error);

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', scopes),
                ["device_code"] = deviceCode,
                ["grant_type"] = DeviceCodeGrant,
            }
        );

        HttpResponseMessage resp = await _http.PostAsync(TokenEndpoint, form, ct);
        if (resp.IsSuccessStatusCode)
        {
            TwitchTokenResponse? json = await resp.Content.ReadFromJsonAsync<TwitchTokenResponse>(
                cancellationToken: ct
            );
            if (json is null)
                return new DevicePollOutcome(DevicePollStatus.Error);

            TokenResult tokens = new(
                json.AccessToken,
                json.RefreshToken,
                _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn),
                json.Scope ?? []
            );
            return new DevicePollOutcome(DevicePollStatus.Authorized, tokens);
        }

        // A still-pending / declined / expired poll comes back as a 4xx whose body names the reason. Twitch
        // carries it in `message` (sometimes `error`), so match on the raw body to tolerate either shaping —
        // the reason tokens are disjoint substrings, so a contains-check can't misclassify.
        string body = await resp.Content.ReadAsStringAsync(ct);
        return ClassifyPendingBody(body);
    }

    private DevicePollOutcome ClassifyPendingBody(string body)
    {
        if (body.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
            return new DevicePollOutcome(DevicePollStatus.Pending);
        if (body.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
            return new DevicePollOutcome(DevicePollStatus.SlowDown);
        if (body.Contains("expired_token", StringComparison.OrdinalIgnoreCase))
            return new DevicePollOutcome(DevicePollStatus.Expired);
        if (body.Contains("access_denied", StringComparison.OrdinalIgnoreCase))
            return new DevicePollOutcome(DevicePollStatus.Denied);

        _logger.LogWarning("Unrecognized device-code poll response: {Body}", body);
        return new DevicePollOutcome(DevicePollStatus.Error);
    }

    private sealed class TwitchDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = null!;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = null!;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = null!;

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = null!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string[]? Scope { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
