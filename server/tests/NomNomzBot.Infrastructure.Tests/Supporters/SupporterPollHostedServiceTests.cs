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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the poll ingress runner (supporter-events.md §0 D3) by its consequences against the REAL ingest
/// path: a tick fetches an enabled poll connection's sealed feed URL and persists each fresh donation as a
/// <c>charity</c> <see cref="SupporterEvent"/> exactly once (a re-poll of the same feed dedups to zero new
/// rows); a conditional re-poll sends <c>If-None-Match</c> and a 304 ingests nothing; a disabled connection
/// never even reaches HTTP; a missing/non-https secret marks the connection <c>error</c> without a request;
/// and a denied run-once lease does nothing at all.
/// </summary>
public sealed class SupporterPollHostedServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-3333-7000-8000-000000000001");
    private const string FeedUrl = "https://www.extra-life.org/api/participants/12345/donations";

    private const string TwoDonations = """
        [
          { "displayName": "Newest", "amount": 10, "donationID": "N-2" },
          { "displayName": "Oldest", "amount": 5, "donationID": "O-1" }
        ]
        """;

    private static async Task<(
        SupporterPollHostedService Service,
        SupporterTestDbContext Db,
        ScriptedHandler Http
    )> BuildAsync(bool enabled = true, string? secret = FeedUrl, IRunOnceGuard? guard = null)
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.SupporterConnections.Add(
            new SupporterConnection
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                SourceKey = "donordrive",
                ConnectionMode = "poll",
                IsEnabled = enabled,
                Status = "idle",
                AuthSecretCipher = secret is null ? null : $"sealed:{secret}",
            }
        );
        await db.SaveChangesAsync();

        SupporterIngestService ingest = new(
            db,
            [new DonordriveSupporterSource()],
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<SupporterIngestService>.Instance
        );

        ServiceCollection services = new();
        services.AddSingleton<IRunOnceGuard>(guard ?? new NoOpRunOnceGuard());
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITokenProtector>(new PrefixProtector());
        services.AddSingleton<ISupporterIngestService>(ingest);
        ServiceProvider provider = services.BuildServiceProvider();

        ScriptedHandler http = new();
        SupporterPollHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new SingleClientFactory(http),
            TimeProvider.System,
            NullLogger<SupporterPollHostedService>.Instance
        );
        return (service, db, http);
    }

    [Fact]
    public async Task PollOnce_IngestsEachDonationOnce_AndARePollDedups()
    {
        (SupporterPollHostedService service, SupporterTestDbContext db, ScriptedHandler http) =
            await BuildAsync();
        http.Enqueue(Ok(TwoDonations, etag: "\"v1\""));
        http.Enqueue(Ok(TwoDonations, etag: "\"v1\"")); // same feed again (no ETag match scripted)

        await service.PollOnceAsync(CancellationToken.None);

        List<SupporterEvent> events = await db.SupporterEvents.ToListAsync();
        events.Should().HaveCount(2);
        events.Select(e => e.Kind).Should().OnlyContain(k => k == "charity");
        events.Select(e => e.ProviderTransactionId).Should().BeEquivalentTo("N-2", "O-1");

        // The same feed re-delivered ingests nothing new — dedup on the provider transaction id.
        http.RespondNotModified = false;
        await service.PollOnceAsync(CancellationToken.None);
        (await db.SupporterEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task PollOnce_SendsIfNoneMatch_AndA304IngestsNothing()
    {
        (SupporterPollHostedService service, SupporterTestDbContext db, ScriptedHandler http) =
            await BuildAsync();
        http.Enqueue(Ok("[]", etag: "\"v7\""));
        http.RespondNotModified = true; // every later request answers 304

        await service.PollOnceAsync(CancellationToken.None);
        await service.PollOnceAsync(CancellationToken.None);

        http.Requests.Should().HaveCount(2);
        // The second request carried the ETag from the first response.
        http.Requests[1].Headers.TryGetValues("If-None-Match", out IEnumerable<string>? etags);
        etags.Should().NotBeNull();
        etags!.Single().Should().Be("\"v7\"");
        (await db.SupporterEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PollOnce_DisabledConnection_NeverReachesHttp()
    {
        (SupporterPollHostedService service, _, ScriptedHandler http) = await BuildAsync(
            enabled: false
        );

        await service.PollOnceAsync(CancellationToken.None);

        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PollOnce_MissingSecret_MarksTheConnectionError_WithoutARequest()
    {
        (SupporterPollHostedService service, SupporterTestDbContext db, ScriptedHandler http) =
            await BuildAsync(secret: null);

        await service.PollOnceAsync(CancellationToken.None);

        http.Requests.Should().BeEmpty();
        (await db.SupporterConnections.SingleAsync()).Status.Should().Be("error");
    }

    [Fact]
    public async Task PollOnce_DeniedLease_DoesNothing()
    {
        IRunOnceGuard denied = Substitute.For<IRunOnceGuard>();
        denied
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((IAsyncDisposable?)null);
        (SupporterPollHostedService service, _, ScriptedHandler http) = await BuildAsync(
            guard: denied
        );

        await service.PollOnceAsync(CancellationToken.None);

        http.Requests.Should().BeEmpty();
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static HttpResponseMessage Ok(string json, string etag)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("ETag", etag);
        return response;
    }

    /// <summary>Serves scripted responses in order and records every request; optional blanket 304 mode.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public bool RespondNotModified { get; set; }

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (RespondNotModified && request.Headers.Contains("If-None-Match"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            return Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                    }
            );
        }
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
