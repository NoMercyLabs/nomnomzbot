// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Supporters.Services;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// Patreon's provider-side provisioning (docs.patreon.com v2 webhooks, scope <c>w:campaigns.webhook</c>):
/// resolves the creator's campaign, finds-or-creates the webhook pointing at the bot's ingest URL for the
/// three <c>members:pledge:*</c> triggers (the source gates on <c>create</c>; update/delete land journaled
/// and are declined downstream), and seals the secret PATREON mints onto the inbound endpoint — the streamer
/// never touches the Patreon portal. Idempotent: an existing webhook for the same URL is reused and its
/// secret re-synced.
/// </summary>
public sealed class PatreonWebhookProvisioner : ISupporterProviderProvisioner
{
    internal const string HttpClientName = "supporter-provision";

    private const string CampaignsUrl = "https://www.patreon.com/api/oauth2/v2/campaigns";
    private const string WebhooksUrl =
        "https://www.patreon.com/api/oauth2/v2/webhooks?fields%5Bwebhook%5D=triggers,uri,secret,paused";
    private const string CreateWebhookUrl = "https://www.patreon.com/api/oauth2/v2/webhooks";

    private static readonly string[] PledgeTriggers =
    [
        "members:pledge:create",
        "members:pledge:update",
        "members:pledge:delete",
    ];

    private readonly IIntegrationTokenVault _vault;
    private readonly IInboundWebhookEndpointService _endpoints;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PatreonWebhookProvisioner> _logger;

    public PatreonWebhookProvisioner(
        IIntegrationTokenVault vault,
        IInboundWebhookEndpointService endpoints,
        IHttpClientFactory httpClientFactory,
        ILogger<PatreonWebhookProvisioner> logger
    )
    {
        _vault = vault;
        _endpoints = endpoints;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string SourceKey => "patreon";

    public async Task<Result> ProvisionAsync(
        Guid broadcasterId,
        Guid integrationConnectionId,
        Guid endpointId,
        string ingestUrl,
        CancellationToken ct = default
    )
    {
        Result<DecryptedTokenDto> token = await _vault.GetAccessTokenAsync(
            integrationConnectionId,
            ct
        );
        if (token.IsFailure)
            return Result.Failure(
                "The Patreon connection has no usable access token — reconnect Patreon.",
                "PROVIDER_NOT_CONNECTED"
            );

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        http.DefaultRequestHeaders.Authorization = new("Bearer", token.Value.Value);

        try
        {
            // The webhook may already exist (an earlier connect, a re-enable) — reuse it and re-sync its secret.
            string? secret = await FindExistingWebhookSecretAsync(http, ingestUrl, ct);
            if (secret is null)
            {
                Result<string> created = await CreateWebhookAsync(http, ingestUrl, ct);
                if (created.IsFailure)
                    return Result.Failure(created.ErrorMessage!, created.ErrorCode!);
                secret = created.Value;
            }

            // The endpoint verifies with the secret PATREON minted — sealed through the normal endpoint path.
            Result<InboundWebhookEndpointDto> sealedSecret = await _endpoints.UpdateAsync(
                broadcasterId,
                endpointId,
                new UpdateInboundWebhookRequest { VerificationSecret = secret },
                ct
            );
            if (sealedSecret.IsFailure)
                return Result.Failure(sealedSecret.ErrorMessage!, sealedSecret.ErrorCode!);

            _logger.LogInformation(
                "Patreon webhook provisioned for {Channel} → {IngestUrl}.",
                broadcasterId,
                ingestUrl
            );
            return Result.Success();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Patreon webhook provisioning failed for {Channel}.",
                broadcasterId
            );
            return Result.Failure(
                "Patreon did not accept the webhook registration — try reconnecting.",
                "PROVISIONING_FAILED"
            );
        }
    }

    /// <summary>The secret of an already-registered webhook pointing at our ingest URL, or null.</summary>
    private static async Task<string?> FindExistingWebhookSecretAsync(
        HttpClient http,
        string ingestUrl,
        CancellationToken ct
    )
    {
        using HttpResponseMessage response = await http.GetAsync(WebhooksUrl, ct);
        if (!response.IsSuccessStatusCode)
            return null; // listing is best-effort — creation below is the authoritative path.

        JObject parsed = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        if (parsed["data"] is not JArray webhooks)
            return null;

        foreach (JToken webhook in webhooks)
        {
            string? uri = webhook.SelectToken("attributes.uri")?.Value<string>();
            if (string.Equals(uri, ingestUrl, StringComparison.OrdinalIgnoreCase))
                return webhook.SelectToken("attributes.secret")?.Value<string>();
        }
        return null;
    }

    /// <summary>Registers the pledge webhook on the creator's campaign; returns the secret Patreon minted.</summary>
    private static async Task<Result<string>> CreateWebhookAsync(
        HttpClient http,
        string ingestUrl,
        CancellationToken ct
    )
    {
        using HttpResponseMessage campaignsResponse = await http.GetAsync(CampaignsUrl, ct);
        if (!campaignsResponse.IsSuccessStatusCode)
            return Result.Failure<string>(
                $"Patreon campaigns read failed ({(int)campaignsResponse.StatusCode}).",
                "PROVISIONING_FAILED"
            );
        JObject campaigns = JObject.Parse(await campaignsResponse.Content.ReadAsStringAsync(ct));
        string? campaignId = (campaigns["data"] as JArray)?.FirstOrDefault()?.Value<string>("id");
        if (string.IsNullOrEmpty(campaignId))
            return Result.Failure<string>(
                "The connected Patreon account has no campaign to subscribe.",
                "NO_CAMPAIGN"
            );

        JObject body = new()
        {
            ["data"] = new JObject
            {
                ["type"] = "webhook",
                ["attributes"] = new JObject
                {
                    ["triggers"] = new JArray(PledgeTriggers),
                    ["uri"] = ingestUrl,
                },
                ["relationships"] = new JObject
                {
                    ["campaign"] = new JObject
                    {
                        ["data"] = new JObject { ["type"] = "campaign", ["id"] = campaignId },
                    },
                },
            },
        };

        using StringContent content = new(
            body.ToString(Formatting.None),
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using HttpResponseMessage createResponse = await http.PostAsync(
            CreateWebhookUrl,
            content,
            ct
        );
        if (!createResponse.IsSuccessStatusCode)
            return Result.Failure<string>(
                $"Patreon webhook creation failed ({(int)createResponse.StatusCode}).",
                "PROVISIONING_FAILED"
            );

        JObject created = JObject.Parse(await createResponse.Content.ReadAsStringAsync(ct));
        string? secret = created.SelectToken("data.attributes.secret")?.Value<string>();
        return string.IsNullOrEmpty(secret)
            ? Result.Failure<string>("Patreon returned no webhook secret.", "PROVISIONING_FAILED")
            : Result.Success(secret);
    }
}
