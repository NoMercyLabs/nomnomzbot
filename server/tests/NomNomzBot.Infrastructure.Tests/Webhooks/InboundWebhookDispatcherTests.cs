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
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NomNomzBot.Infrastructure.Webhooks.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves the inbound dispatcher (webhooks.md §3.2): an unknown token is 404 with NO bus event; a verified GitHub
/// event is journaled (Source=webhook) and fans out a single received event + bumps the counter; a duplicate
/// (journal already has the deterministic EventId) is 200 without re-journaling or re-emitting; a bad signature is
/// 4xx + a rejected event; a disabled endpoint is 503.
/// </summary>
public sealed class InboundWebhookDispatcherTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000b01");
    private static readonly Guid Endpoint = Guid.Parse("0192a000-0000-7000-8000-000000000b02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Secret = "gh-secret";

    private static (
        InboundWebhookDispatcher Sut,
        AuthDbContext Db,
        IEventJournal Journal,
        RecordingEventBus Bus
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Secret);
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<EventRecord>("Not found.", "NOT_FOUND")); // default: not a duplicate
        journal
            .AppendAsync(Arg.Any<AppendEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success(Record(ci.ArgAt<AppendEventRequest>(0).EventId)));
        RecordingEventBus bus = new();
        InboundSignatureVerifier verifier = new(new FakeTimeProvider(Now));
        IInboundWebhookAdapter[] adapters =
        [
            new KofiInboundWebhookAdapter(),
            new GithubInboundWebhookAdapter(verifier),
            new GenericInboundWebhookAdapter(verifier),
        ];
        InboundWebhookDispatcher sut = new(
            db,
            protector,
            adapters,
            journal,
            bus,
            new FakeTimeProvider(Now)
        );
        return (sut, db, journal, bus);
    }

    private static EventRecord Record(Guid eventId) =>
        new(
            1,
            eventId,
            Channel,
            42,
            "webhook.github.push",
            1,
            "webhook",
            "{}",
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            Now.UtcDateTime,
            Now.UtcDateTime
        );

    private static async Task SeedGithubAsync(AuthDbContext db, bool enabled = true)
    {
        db.InboundWebhookEndpoints.Add(
            new InboundWebhookEndpoint
            {
                Id = Endpoint,
                BroadcasterId = Channel,
                Name = "gh",
                Token = "tok123",
                AdapterKind = WebhookAdapterKind.Github,
                VerificationSecretEnvelope = "sealed",
                EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-000000000bbb"),
                IsEnabled = enabled,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    private static InboundWebhookRequest GithubRequest(bool validSignature = true)
    {
        byte[] body = Encoding.UTF8.GetBytes("{\"action\":\"opened\"}");
        string signature =
            "sha256="
            + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body));
        return new InboundWebhookRequest
        {
            Token = "tok123",
            Method = "POST",
            ContentType = "application/json",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Hub-Signature-256"] = validSignature ? signature : "sha256=deadbeef",
                ["X-GitHub-Event"] = "push",
                ["X-GitHub-Delivery"] = "del_1",
            },
            RawBody = body,
            ReceivedAtUtc = Now.UtcDateTime,
            RemoteIpHash = "iphash",
        };
    }

    [Fact]
    public async Task An_unknown_token_is_404_with_no_bus_event()
    {
        (InboundWebhookDispatcher sut, _, _, RecordingEventBus bus) = Build();

        InboundDispatchResult result = (await sut.DispatchAsync(GithubRequest())).Value;

        result.HttpStatus.Should().Be(404);
        result.RejectReason.Should().Be(WebhookRejectReason.UnknownEndpoint);
        bus.Published.Should().BeEmpty(); // no amplification on the unknown-token path
    }

    [Fact]
    public async Task A_verified_event_is_journaled_and_fans_out()
    {
        (
            InboundWebhookDispatcher sut,
            AuthDbContext db,
            IEventJournal journal,
            RecordingEventBus bus
        ) = Build();
        await SeedGithubAsync(db);

        InboundDispatchResult result = (await sut.DispatchAsync(GithubRequest())).Value;

        result.Verified.Should().BeTrue();
        result.WasDuplicate.Should().BeFalse();
        result.HttpStatus.Should().Be(200);
        result.EventType.Should().Be("webhook.github.push");
        await journal
            .Received()
            .AppendAsync(Arg.Any<AppendEventRequest>(), Arg.Any<CancellationToken>());
        bus.Published.OfType<InboundWebhookReceivedEvent>().Should().ContainSingle();
        db.InboundWebhookEndpoints.Single().ReceiveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_duplicate_event_is_200_without_re_journaling()
    {
        (
            InboundWebhookDispatcher sut,
            AuthDbContext db,
            IEventJournal journal,
            RecordingEventBus bus
        ) = Build();
        await SeedGithubAsync(db);
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Record(Guid.NewGuid()))); // already journaled

        InboundDispatchResult result = (await sut.DispatchAsync(GithubRequest())).Value;

        result.WasDuplicate.Should().BeTrue();
        result.HttpStatus.Should().Be(200);
        await journal
            .DidNotReceive()
            .AppendAsync(Arg.Any<AppendEventRequest>(), Arg.Any<CancellationToken>());
        bus.Published.OfType<InboundWebhookReceivedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_bad_signature_is_4xx_and_emits_a_rejected_event()
    {
        (InboundWebhookDispatcher sut, AuthDbContext db, _, RecordingEventBus bus) = Build();
        await SeedGithubAsync(db);

        InboundDispatchResult result = (
            await sut.DispatchAsync(GithubRequest(validSignature: false))
        ).Value;

        result.Verified.Should().BeFalse();
        result.HttpStatus.Should().Be(401);
        result.RejectReason.Should().Be(WebhookRejectReason.InvalidSignature);
        bus.Published.OfType<InboundWebhookRejectedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_disabled_endpoint_is_503()
    {
        (InboundWebhookDispatcher sut, AuthDbContext db, _, _) = Build();
        await SeedGithubAsync(db, enabled: false);

        InboundDispatchResult result = (await sut.DispatchAsync(GithubRequest())).Value;

        result.HttpStatus.Should().Be(503);
        result.RejectReason.Should().Be(WebhookRejectReason.Disabled);
    }
}
