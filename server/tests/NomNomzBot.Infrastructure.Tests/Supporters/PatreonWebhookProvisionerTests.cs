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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the Patreon provider-side provisioning (docs.patreon.com v2 webhooks) by its consequence — the
/// inbound endpoint ends up sealed with the secret PATREON minted: a fresh connect resolves the campaign,
/// creates the pledge webhook against the bot's ingest URL, and seals the returned secret; a re-provision
/// finds the existing webhook for the same URL and re-syncs its secret without creating a duplicate; a
/// campaign-less account and a missing vault token fail with actionable codes.
/// </summary>
public sealed class PatreonWebhookProvisionerTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-5555-7000-8000-000000000001");
    private static readonly Guid OAuthConnection = Guid.Parse(
        "019f2900-5555-7000-8000-0000000000aa"
    );

    private const string CampaignsJson =
        """{ "data": [ { "type": "campaign", "id": "c-777" } ] }""";
    private const string EmptyWebhooksJson = """{ "data": [] }""";
    private const string CreatedWebhookJson = """
        { "data": { "type": "webhook", "id": "wh-1", "attributes": { "secret": "patreon-minted-secret", "uri": "IGNORED" } } }
        """;

    private static async Task<(
        PatreonWebhookProvisioner Provisioner,
        SupporterTestDbContext Db,
        Guid EndpointId,
        string IngestUrl,
        ScriptedPatreonHandler Http
    )> BuildAsync(ScriptedPatreonHandler http, bool vaultHasToken = true)
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();

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

        // The endpoint the connection service would have created — placeholder-sealed.
        Result<NomNomzBot.Application.DTOs.Webhooks.InboundWebhookEndpointDto> endpoint =
            await endpoints.CreateAsync(
                Tenant,
                Guid.NewGuid(),
                new NomNomzBot.Application.DTOs.Webhooks.CreateInboundWebhookRequest
                {
                    Name = "patreon (supporters)",
                    Adapter = WebhookAdapterKind.Patreon,
                    VerificationSecret = "placeholder",
                    IsEnabled = true,
                }
            );

        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .GetAccessTokenAsync(OAuthConnection, Arg.Any<CancellationToken>())
            .Returns(
                vaultHasToken
                    ? Result.Success(
                        new DecryptedTokenDto("patreon-access-token", "Bearer", null, false)
                    )
                    : Result.Failure<DecryptedTokenDto>("No token.", "NOT_FOUND")
            );

        PatreonWebhookProvisioner provisioner = new(
            vault,
            endpoints,
            new SingleClientFactory(http),
            NullLogger<PatreonWebhookProvisioner>.Instance
        );
        return (provisioner, db, endpoint.Value.Id, endpoint.Value.IngestUrl, http);
    }

    [Fact]
    public async Task Provision_FreshConnect_CreatesTheWebhook_AndSealsPatreonsSecret()
    {
        ScriptedPatreonHandler http = new()
        {
            WebhooksListJson = EmptyWebhooksJson,
            CampaignsJson = CampaignsJson,
            CreateWebhookJson = CreatedWebhookJson,
        };
        (
            PatreonWebhookProvisioner provisioner,
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
        // The real consequence: the endpoint now verifies with the secret PATREON minted.
        InboundWebhookEndpoint endpoint = await db.InboundWebhookEndpoints.SingleAsync();
        endpoint.VerificationSecretEnvelope.Should().Be("sealed:patreon-minted-secret");
        // The creation body targeted our ingest URL with the three pledge triggers on the campaign.
        http.LastCreateBody.Should().Contain(ingestUrl);
        http.LastCreateBody.Should().Contain("members:pledge:create");
        http.LastCreateBody.Should().Contain("\"id\":\"c-777\"");
        // The API rode the vaulted token.
        http.LastAuthorization.Should().Be("Bearer patreon-access-token");
    }

    [Fact]
    public async Task Provision_ExistingWebhookForTheSameUrl_ReusesIt_AndResyncsTheSecret()
    {
        ScriptedPatreonHandler http = new() { CampaignsJson = CampaignsJson };
        (
            PatreonWebhookProvisioner provisioner,
            SupporterTestDbContext db,
            Guid endpointId,
            string ingestUrl,
            _
        ) = await BuildAsync(http);
        http.WebhooksListJson = $$"""
            { "data": [ { "type": "webhook", "id": "wh-9", "attributes": { "uri": "{{ingestUrl}}", "secret": "already-minted" } } ] }
            """;

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await db.InboundWebhookEndpoints.SingleAsync())
            .VerificationSecretEnvelope.Should()
            .Be("sealed:already-minted");
        http.CreateCalls.Should().Be(0, "the existing registration is reused, never duplicated");
    }

    [Fact]
    public async Task Provision_NoCampaign_FailsActionably()
    {
        ScriptedPatreonHandler http = new()
        {
            WebhooksListJson = EmptyWebhooksJson,
            CampaignsJson = """{ "data": [] }""",
        };
        (PatreonWebhookProvisioner provisioner, _, Guid endpointId, string ingestUrl, _) =
            await BuildAsync(http);

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NO_CAMPAIGN");
    }

    [Fact]
    public async Task Provision_NoVaultToken_FailsAsNotConnected()
    {
        ScriptedPatreonHandler http = new();
        (PatreonWebhookProvisioner provisioner, _, Guid endpointId, string ingestUrl, _) =
            await BuildAsync(http, vaultHasToken: false);

        Result result = await provisioner.ProvisionAsync(
            Tenant,
            OAuthConnection,
            endpointId,
            ingestUrl
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_CONNECTED");
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    /// <summary>Scripts the three Patreon API routes and records the creation body + auth header.</summary>
    private sealed class ScriptedPatreonHandler : HttpMessageHandler
    {
        public string CampaignsJson { get; set; } = """{ "data": [] }""";
        public string WebhooksListJson { get; set; } = """{ "data": [] }""";
        public string CreateWebhookJson { get; set; } = """{ "data": {} }""";
        public string? LastCreateBody { get; private set; }
        public string? LastAuthorization { get; private set; }
        public int CreateCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            string path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post && path.EndsWith("/webhooks"))
            {
                CreateCalls++;
                LastCreateBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(CreateWebhookJson);
            }
            if (path.EndsWith("/webhooks"))
                return Json(WebhooksListJson);
            if (path.EndsWith("/campaigns"))
                return Json(CampaignsJson);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
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
