// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves outbound webhook endpoint CRUD (webhooks.md §3.5): create fails closed unless the Fqdn matches an enabled
/// H.7 egress-allowlist row, otherwise it seals the minted whsec_ secret (revealing plaintext once) and pins the
/// allowlist row; rotate promotes the primary to secondary and mints a fresh primary; re-enable clears the failure
/// counters; and the synthetic test delivery is unavailable pending the egress client.
/// </summary>
public sealed class OutboundWebhookEndpointServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000c01");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000c02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (OutboundWebhookEndpointService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult($"sealed:{ci.ArgAt<string>(0)}"));
        ISubjectKeyService keys = Substitute.For<ISubjectKeyService>();
        keys.GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.Parse("0192a000-0000-7000-8000-0000000000cc")));
        return (
            new OutboundWebhookEndpointService(db, protector, keys, new FakeTimeProvider(Now)),
            db
        );
    }

    private static async Task SeedAllowlistAsync(AuthDbContext db, string fqdn = "api.example.com")
    {
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
            {
                BroadcasterId = Channel,
                Fqdn = fqdn,
                IsEnabled = true,
                MaxResponseBytes = 65536,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    private static CreateOutboundWebhookRequest Req(string fqdn = "api.example.com") =>
        new()
        {
            Name = "endpoint",
            Fqdn = fqdn,
            SubscribedEventTypes = ["*"],
        };

    [Fact]
    public async Task Create_fails_closed_without_an_egress_allowlist_row()
    {
        (OutboundWebhookEndpointService sut, _) = Build();

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            Req()
        );

        result.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
    }

    [Fact]
    public async Task Create_seals_the_secret_pins_the_allowlist_and_reveals_plaintext_once()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db) = Build();
        await SeedAllowlistAsync(db);

        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        created.SigningSecret.Should().StartWith("whsec_");
        created.Endpoint.SubscribedEventTypes.Should().Contain("*");
        OutboundWebhookEndpoint stored = db.OutboundWebhookEndpoints.Single();
        stored.SigningSecretEnvelope.Should().StartWith("sealed:whsec_"); // sealed, not plaintext
        stored.HttpEgressAllowlistId.Should().NotBeNull();
    }

    [Fact]
    public async Task RotateSecret_promotes_the_primary_to_secondary_and_mints_a_new_primary()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        string originalEnvelope = db.OutboundWebhookEndpoints.Single().SigningSecretEnvelope;

        OutboundWebhookEndpointCreatedDto rotated = (
            await sut.RotateSecretAsync(Channel, created.Endpoint.Id)
        ).Value;

        rotated.SigningSecret.Should().StartWith("whsec_");
        OutboundWebhookEndpoint stored = db.OutboundWebhookEndpoints.Single();
        stored.SecondarySigningSecretEnvelope.Should().Be(originalEnvelope);
        stored.SigningSecretEnvelope.Should().NotBe(originalEnvelope);
    }

    [Fact]
    public async Task Reenable_clears_the_failure_counters()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        OutboundWebhookEndpoint endpoint = db.OutboundWebhookEndpoints.Single();
        endpoint.IsEnabled = false;
        endpoint.ConsecutiveFailureCount = 20;
        endpoint.DisabledAt = Now.UtcDateTime;
        await db.SaveChangesAsync();

        await sut.ReenableAsync(Channel, created.Endpoint.Id);

        OutboundWebhookEndpoint after = db.OutboundWebhookEndpoints.Single();
        after.IsEnabled.Should().BeTrue();
        after.ConsecutiveFailureCount.Should().Be(0);
        after.DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task SendTest_is_unavailable_pending_the_egress_client()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        (await sut.SendTestAsync(Channel, created.Endpoint.Id))
            .ErrorCode.Should()
            .Be("SERVICE_UNAVAILABLE");
    }
}
