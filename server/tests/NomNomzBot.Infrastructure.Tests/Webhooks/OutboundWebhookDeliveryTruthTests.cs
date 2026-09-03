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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// S099a: proves the two outbound-webhook truth fixes.
/// (1) <see cref="OutboundWebhookBackoffPolicy"/> caps and jitters retry delay — no unbounded exponential
/// growth, no lockstep thundering-herd retries.
/// (2) <see cref="OutboundWebhookFanoutHandler"/> no longer blocks the event-publishing call on the actual
/// HTTP delivery, and the delivery's <c>Result</c> is observed (not discarded) once it completes.
/// </summary>
public sealed class OutboundWebhookDeliveryTruthTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid EndpointId = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A HANG detector, not a timing assertion. Nothing here is expected to take a measurable amount of
    /// time; this bound only exists so a genuinely stuck await fails the test instead of hanging the
    /// suite. It is deliberately generous — a slow machine must not turn into a red build.
    /// </summary>
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Backoff_never_exceeds_the_one_hour_cap_even_for_a_huge_attempt_number()
    {
        TimeSpan delay = OutboundWebhookBackoffPolicy.ComputeDelay(attempt: 50, new Random(1));

        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(1));
        // Never zero either — a capped-but-jittered-to-zero delay would still thundering-herd on the floor.
        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Backoff_is_jittered_two_calls_for_the_same_attempt_can_differ()
    {
        TimeSpan first = OutboundWebhookBackoffPolicy.ComputeDelay(attempt: 10, new Random(1));
        TimeSpan second = OutboundWebhookBackoffPolicy.ComputeDelay(attempt: 10, new Random(2));

        first.Should().NotBe(second);
    }

    /// <summary>HTTP handler that blocks on a caller-controlled gate until the test releases it, so the test
    /// can observe whether the fanout handler waited on it or returned before it completed.</summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _releaseGate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource<bool> RequestReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _releaseGate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestReceived.TrySetResult(true);
            await _releaseGate.Task; // held open until the test calls Release()
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }

    private static (OutboundWebhookFanoutHandler Handler, GatedHandler HttpHandler) Build(
        string databaseName
    )
    {
        AuthDbContext seedDb = AuthTestBuilder.NewContext(databaseName);
        seedDb.OutboundWebhookEndpoints.Add(
            new OutboundWebhookEndpoint
            {
                Id = EndpointId,
                BroadcasterId = Channel,
                Name = "ep",
                Fqdn = "api.example.com",
                SubscribedEventTypesJson = JsonConvert.SerializeObject(new[] { "*" }),
                SigningSecretEnvelope = "sealed",
                EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-000000000e03"),
                IsEnabled = true,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        seedDb.SaveChanges();

        GatedHandler gatedHandler = new();
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new(gatedHandler));

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("whsec_secret");
        IWebhookBodyTemplateRenderer template = Substitute.For<IWebhookBodyTemplateRenderer>();
        template
            .Render(
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<bool>()
            )
            .Returns(ci => ci.ArgAt<string?>(0) ?? string.Empty);

        // A fresh IServiceScopeFactory whose scopes each hand back a fresh context/connection against the
        // SAME shared-cache database — mirrors the production DI graph the fanout handler's background
        // Task.Run resolves its own scoped dispatcher from.
        ServiceCollection services = new();
        services.AddScoped(_ => AuthTestBuilder.NewContext(databaseName));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddSingleton(protector);
        services.AddSingleton<IOutboundWebhookSigner>(new OutboundWebhookSigner());
        services.AddSingleton(template);
        services.AddSingleton(httpClientFactory);
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(
            new RecordingEventBus()
        );
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddScoped<IOutboundWebhookDispatcher, OutboundWebhookDispatcher>();
        ServiceProvider provider = services.BuildServiceProvider();

        OutboundWebhookFanoutHandler handler = new(
            seedDb,
            NullLogger<OutboundWebhookFanoutHandler>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        return (handler, gatedHandler);
    }

    [Fact]
    public async Task Fanout_returns_before_the_slow_http_send_completes_and_still_records_the_failed_attempt()
    {
        string databaseName = Guid.NewGuid().ToString();
        (OutboundWebhookFanoutHandler handler, GatedHandler gatedHandler) = Build(databaseName);

        EventRecord committed = new(
            Id: 1,
            EventId: Guid.CreateVersion7(),
            BroadcasterId: Channel,
            StreamPosition: 1,
            EventType: "test.event",
            EventVersion: 1,
            Source: "test",
            PayloadJson: "{}",
            PayloadIsEncrypted: false,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: Now.UtcDateTime,
            RecordedAt: Now.UtcDateTime
        );

        Task<Result> onCommitted = handler.OnCommittedAsync(committed);

        // The publish-side call completes WITHOUT waiting on the gated HTTP send — the send is still
        // blocked in GatedHandler at this point, so if the publisher were awaiting it this would time
        // out. That is the real proof the delivery moved off the publishing thread; racing it against
        // a Task.Delay only measured how busy the machine was.
        (await onCommitted.WaitAsync(HangTimeout)).IsSuccess.Should().BeTrue();

        // The background delivery does reach the HTTP client — it just isn't awaited by the publisher.
        // Awaited as a SIGNAL, not raced against a wall clock: the old form
        // (WhenAny against Task.Delay(5s), asserting which won) turned a loaded machine into a red
        // suite roughly half the time. The timeout below is a hang detector, not the assertion — if
        // it ever fires, the send genuinely never happened.
        await gatedHandler.RequestReceived.Task.WaitAsync(HangTimeout);

        // Let the gated send fail (500) and then await the ACTUAL detached delivery task rather than
        // polling for the row to appear. When it completes, the outcome has been persisted.
        gatedHandler.Release();
        handler.LastDispatch.Should().NotBeNull();
        await handler.LastDispatch!.WaitAsync(HangTimeout);

        AuthDbContext assertDb = AuthTestBuilder.NewContext(databaseName);
        OutboundWebhookDelivery? delivery = await assertDb
            .OutboundWebhookDeliveries.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EndpointId == EndpointId);

        // The Result of the delivery attempt was observed and recorded, not silently dropped: a delivery row
        // exists with the failure captured, not left as an untouched Pending stub.
        delivery.Should().NotBeNull();
        delivery.Error.Should().NotBeNullOrEmpty();
        delivery.NextRetryAt.Should().NotBeNull();
    }
}
