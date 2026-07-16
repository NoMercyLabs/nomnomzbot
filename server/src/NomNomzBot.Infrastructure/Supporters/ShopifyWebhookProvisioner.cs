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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// Shopify's provider-side provisioning (shopify.dev Admin API webhooks): resolves the connected shop's
/// domain from the OAuth connection, finds-or-creates the <c>orders/create</c> webhook subscription pointing
/// at the bot's ingest URL, and seals the APP CLIENT SECRET onto the inbound endpoint — Shopify signs every
/// delivery's <c>X-Shopify-Hmac-SHA256</c> with the app secret (there is no per-webhook secret), which is
/// exactly what <c>ShopifyInboundWebhookAdapter</c> verifies with. Idempotent: an existing subscription for
/// the same address is reused, never duplicated.
/// </summary>
public sealed class ShopifyWebhookProvisioner : ISupporterProviderProvisioner
{
    private const string ApiVersion = "2025-10";

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly IInboundWebhookEndpointService _endpoints;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ShopifyWebhookProvisioner> _logger;

    public ShopifyWebhookProvisioner(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        IInboundWebhookEndpointService endpoints,
        ISystemCredentialsProvider credentials,
        IHttpClientFactory httpClientFactory,
        ILogger<ShopifyWebhookProvisioner> logger
    )
    {
        _db = db;
        _vault = vault;
        _endpoints = endpoints;
        _credentials = credentials;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string SourceKey => "shopify";

    public async Task<Result> ProvisionAsync(
        Guid broadcasterId,
        Guid integrationConnectionId,
        Guid endpointId,
        string ingestUrl,
        CancellationToken ct = default
    )
    {
        // The shop domain was remembered on the OAuth connection at connect time.
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == integrationConnectionId && c.DeletedAt == null, ct);
        string? shop = ReadShopName(connection?.Settings);
        if (connection is null || shop is null)
            return Result.Failure(
                "The Shopify connection carries no shop — reconnect Shopify.",
                "PROVIDER_NOT_CONNECTED"
            );

        Result<DecryptedTokenDto> token = await _vault.GetAccessTokenAsync(
            integrationConnectionId,
            ct
        );
        if (token.IsFailure)
            return Result.Failure(
                "The Shopify connection has no usable access token — reconnect Shopify.",
                "PROVIDER_NOT_CONNECTED"
            );

        // Shopify signs deliveries with the APP client secret — that IS the endpoint's verification secret.
        SystemAppCredentials? app = await _credentials.GetAsync(
            AuthEnums.IntegrationProvider.Shopify,
            ct
        );
        if (app is null)
            return Result.Failure(
                "Shopify app credentials are not configured.",
                "PROVIDER_NOT_CONFIGURED"
            );

        string apiBase = $"https://{shop}.myshopify.com/admin/api/{ApiVersion}";
        HttpClient http = _httpClientFactory.CreateClient(PatreonWebhookProvisioner.HttpClientName);
        http.DefaultRequestHeaders.Add("X-Shopify-Access-Token", token.Value.Value);

        try
        {
            if (!await SubscriptionExistsAsync(http, apiBase, ingestUrl, ct))
            {
                Result created = await CreateSubscriptionAsync(http, apiBase, ingestUrl, ct);
                if (created.IsFailure)
                    return created;
            }

            Result<InboundWebhookEndpointDto> sealedSecret = await _endpoints.UpdateAsync(
                broadcasterId,
                endpointId,
                new UpdateInboundWebhookRequest { VerificationSecret = app.ClientSecret },
                ct
            );
            if (sealedSecret.IsFailure)
                return Result.Failure(sealedSecret.ErrorMessage!, sealedSecret.ErrorCode!);

            _logger.LogInformation(
                "Shopify orders webhook provisioned for {Channel} on {Shop} → {IngestUrl}.",
                broadcasterId,
                shop,
                ingestUrl
            );
            return Result.Success();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Shopify webhook provisioning failed for {Channel}.",
                broadcasterId
            );
            return Result.Failure(
                "Shopify did not accept the webhook registration — try reconnecting.",
                "PROVISIONING_FAILED"
            );
        }
    }

    /// <summary>The sanitized shop name remembered at connect (<c>{"shopDomain":"my-store"}</c>), or null.</summary>
    private static string? ReadShopName(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
            return null;
        try
        {
            return JObject.Parse(settingsJson).Value<string>("shopDomain");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> SubscriptionExistsAsync(
        HttpClient http,
        string apiBase,
        string ingestUrl,
        CancellationToken ct
    )
    {
        using HttpResponseMessage response = await http.GetAsync($"{apiBase}/webhooks.json", ct);
        if (!response.IsSuccessStatusCode)
            return false; // listing is best-effort — creation below is the authoritative path.

        JObject parsed = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        if (parsed["webhooks"] is not JArray webhooks)
            return false;

        foreach (JToken webhook in webhooks)
            if (
                string.Equals(
                    webhook.Value<string>("address"),
                    ingestUrl,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;
        return false;
    }

    private static async Task<Result> CreateSubscriptionAsync(
        HttpClient http,
        string apiBase,
        string ingestUrl,
        CancellationToken ct
    )
    {
        JObject body = new()
        {
            ["webhook"] = new JObject
            {
                ["topic"] = "orders/create",
                ["address"] = ingestUrl,
                ["format"] = "json",
            },
        };
        using StringContent content = new(
            body.ToString(Formatting.None),
            System.Text.Encoding.UTF8,
            "application/json"
        );
        using HttpResponseMessage response = await http.PostAsync(
            $"{apiBase}/webhooks.json",
            content,
            ct
        );
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(
                $"Shopify webhook creation failed ({(int)response.StatusCode}).",
                "PROVISIONING_FAILED"
            );
    }
}
