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
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the Shopify provider-side provisioning (shopify.dev Admin API webhooks) by its consequence — the
/// inbound endpoint ends up sealed with the APP CLIENT SECRET (what Shopify signs every delivery's
/// <c>X-Shopify-Hmac-SHA256</c> with; there is no per-webhook secret): a fresh connect creates the
/// <c>orders/create</c> subscription on the connected shop's domain over the vaulted token; an existing
/// subscription for the same address is reused, never duplicated; a connection with no remembered shop
/// fails actionably.
/// </summary>
public sealed class ShopifyWebhookProvisionerTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-6666-7000-8000-000000000001");
    private static readonly Guid OAuthConnection = Guid.Parse(
        "019f2900-6666-7000-8000-0000000000aa"
    );

    private static async Task<(
        ShopifyWebhookProvisioner Provisioner,
        SupporterTestDbContext Db,
        Guid EndpointId,
        string IngestUrl,
        ScriptedShopifyHandler Http
    )> BuildAsync(
        ScriptedShopifyHandler http,
        string? settingsJson = """{"shopDomain":"my-store"}"""
    )
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                Id = OAuthConnection,
                BroadcasterId = Tenant,
                Provider = AuthEnums.IntegrationProvider.Shopify,
                Status = AuthEnums.IntegrationStatus.Connected,
                Settings = settingsJson,
            }
        );
        await db.SaveChangesAsync();

        ISubjectKeyService subjectKeys = Substitute.For<ISubjectKeyService>();
        subjectKeys
            .GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.NewGuid()));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://bot.example.test" }
            )
            .Build();
        InboundWebhookEndpointService endpoints = new(
            db,
            new PrefixProtector(),
            subjectKeys,
            config,
            TimeProvider.System,
            new RecordingEventBus()
        );
        Result<NomNomzBot.Application.DTOs.Webhooks.InboundWebhookEndpointDto> endpoint =
            await endpoints.CreateAsync(
                Tenant,
                Guid.NewGuid(),
                new NomNomzBot.Application.DTOs.Webhooks.CreateInboundWebhookRequest
                {
                    Name = "shopify (supporters)",
                    Adapter = WebhookAdapterKind.Shopify,
                    VerificationSecret = "placeholder",
                    IsEnabled = true,
                }
            );

        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .GetAccessTokenAsync(OAuthConnection, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto("shpat-access", "Bearer", null, false)));

        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetAsync(AuthEnums.IntegrationProvider.Shopify, Arg.Any<CancellationToken>())
            .Returns(new SystemAppCredentials("shopify-client", "shopify-app-secret"));

        ShopifyWebhookProvisioner provisioner = new(
            db,
            vault,
            endpoints,
            credentials,
            new SingleClientFactory(http),
            NullLogger<ShopifyWebhookProvisioner>.Instance
        );
        return (provisioner, db, endpoint.Value.Id, endpoint.Value.IngestUrl, http);
    }

    [Fact]
    public async Task Provision_FreshConnect_CreatesTheSubscription_AndSealsTheAppSecret()
    {
        ScriptedShopifyHandler http = new() { WebhooksListJson = """{ "webhooks": [] }""" };
        (
            ShopifyWebhookProvisioner provisioner,
            SupporterTestDbContext db,
            Guid endpointId,
            string ingestUrl,
            _
        ) = await BuildAsync(http);

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // Shopify signs deliveries with the APP secret — that is now the endpoint's verification secret.
        InboundWebhookEndpoint endpoint = await db.InboundWebhookEndpoints.SingleAsync();
        endpoint.VerificationSecretEnvelope.Should().Be("sealed:shopify-app-secret");
        // The subscription was created on the SHOP's domain, over the vaulted token, for orders/create.
        http.LastRequestHost.Should().Be("my-store.myshopify.com");
        http.LastAccessTokenHeader.Should().Be("shpat-access");
        http.LastCreateBody.Should().Contain("orders/create").And.Contain(ingestUrl);
    }

    [Fact]
    public async Task Provision_ExistingSubscriptionForTheSameAddress_IsReused()
    {
        ScriptedShopifyHandler http = new();
        (
            ShopifyWebhookProvisioner provisioner,
            SupporterTestDbContext db,
            Guid endpointId,
            string ingestUrl,
            _
        ) = await BuildAsync(http);
        http.WebhooksListJson = $$"""
            { "webhooks": [ { "id": 42, "topic": "orders/create", "address": "{{ingestUrl}}" } ] }
            """;

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        http.CreateCalls.Should().Be(0, "the existing subscription is reused, never duplicated");
        (await db.InboundWebhookEndpoints.SingleAsync())
            .VerificationSecretEnvelope.Should()
            .Be("sealed:shopify-app-secret", "the app secret still seals on the reuse path");
    }

    [Fact]
    public async Task Provision_ConnectionWithoutARememberedShop_FailsActionably()
    {
        ScriptedShopifyHandler http = new();
        (ShopifyWebhookProvisioner provisioner, _, Guid endpointId, string ingestUrl, _) =
            await BuildAsync(http, settingsJson: null);

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_CONNECTED");
        http.CreateCalls.Should().Be(0);
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    /// <summary>Scripts the Admin API webhooks routes and records the creation body, host, and token header.</summary>
    private sealed class ScriptedShopifyHandler : HttpMessageHandler
    {
        public string WebhooksListJson { get; set; } = """{ "webhooks": [] }""";
        public string? LastCreateBody { get; private set; }
        public string? LastRequestHost { get; private set; }
        public string? LastAccessTokenHeader { get; private set; }
        public int CreateCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequestHost = request.RequestUri!.Host;
            LastAccessTokenHeader = request.Headers.TryGetValues(
                "X-Shopify-Access-Token",
                out IEnumerable<string>? values
            )
                ? values.FirstOrDefault()
                : null;

            if (request.Method == HttpMethod.Post)
            {
                CreateCalls++;
                LastCreateBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("""{ "webhook": { "id": 99 } }""", HttpStatusCode.Created);
            }
            return Json(WebhooksListJson, HttpStatusCode.OK);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Transparent AEAD stand-in (<c>sealed:&lt;plaintext&gt;</c>) — the crypto is proven elsewhere.</summary>
    private sealed class PrefixProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"sealed:{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? sealedEnvelope,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                sealedEnvelope is not null && sealedEnvelope.StartsWith("sealed:")
                    ? sealedEnvelope["sealed:".Length..]
                    : null
            );
    }
}
