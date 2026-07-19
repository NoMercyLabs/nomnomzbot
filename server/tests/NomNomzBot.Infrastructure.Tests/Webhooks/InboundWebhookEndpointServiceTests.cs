// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves inbound webhook endpoint CRUD (webhooks.md §3.1): create mints a 64-char token + seals the verification
/// secret (never persisting or returning plaintext); a generic adapter demands a config; token rotation changes
/// the ingest URL; update re-seals only when a new secret is supplied; and delete soft-deletes.
/// </summary>
public sealed class InboundWebhookEndpointServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000a01");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000a02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (
        InboundWebhookEndpointService Sut,
        AuthDbContext Db,
        ITokenProtector Protector,
        RecordingEventBus Bus
    ) Build()
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
            .Returns(Result.Success(Guid.Parse("0192a000-0000-7000-8000-0000000000aa")));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://bot.example" }
            )
            .Build();
        RecordingEventBus bus = new();
        InboundWebhookEndpointService sut = new(
            db,
            protector,
            keys,
            config,
            new FakeTimeProvider(Now),
            bus
        );
        return (sut, db, protector, bus);
    }

    private static CreateInboundWebhookRequest Req(
        WebhookAdapterKind adapter = WebhookAdapterKind.Kofi,
        string secret = "topsecret"
    ) =>
        new()
        {
            Name = "endpoint",
            Adapter = adapter,
            VerificationSecret = secret,
        };

    [Fact]
    public async Task Create_mints_a_token_seals_the_secret_and_never_returns_plaintext()
    {
        (InboundWebhookEndpointService sut, AuthDbContext db, ITokenProtector protector, _) =
            Build();

        InboundWebhookEndpointDto dto = (await sut.CreateAsync(Channel, Actor, Req())).Value;

        dto.IngestUrl.Should().StartWith("https://bot.example/api/v1/webhooks/in/");
        dto.VerificationSecretSet.Should().BeTrue();
        InboundWebhookEndpoint stored = db.InboundWebhookEndpoints.Single();
        stored.Token.Should().HaveLength(64);
        stored.VerificationSecretEnvelope.Should().Be("sealed:topsecret"); // sealed, not plaintext
        await protector
            .Received()
            .ProtectAsync(
                "topsecret",
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Create_rejects_a_generic_adapter_without_a_config()
    {
        (InboundWebhookEndpointService sut, _, _, RecordingEventBus bus) = Build();

        Result<InboundWebhookEndpointDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            Req(WebhookAdapterKind.Generic)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        bus.Published.Should().BeEmpty(); // a failed mutation publishes nothing
    }

    [Fact]
    public async Task Create_publishes_ChannelConfigChangedEvent_for_the_webhooks_domain()
    {
        (InboundWebhookEndpointService sut, _, _, RecordingEventBus bus) = Build();

        InboundWebhookEndpointDto created = (await sut.CreateAsync(Channel, Actor, Req())).Value;

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "webhooks"
                && e.EntityId == created.Id.ToString()
                && e.Action == "created"
            );
    }

    [Fact]
    public async Task RotateToken_changes_the_ingest_url()
    {
        (InboundWebhookEndpointService sut, _, _, _) = Build();
        InboundWebhookEndpointDto created = (await sut.CreateAsync(Channel, Actor, Req())).Value;

        InboundWebhookEndpointDto rotated = (await sut.RotateTokenAsync(Channel, created.Id)).Value;

        rotated.IngestUrl.Should().NotBe(created.IngestUrl);
    }

    [Fact]
    public async Task Update_reseals_only_when_a_new_secret_is_supplied()
    {
        (InboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        InboundWebhookEndpointDto created = (await sut.CreateAsync(Channel, Actor, Req())).Value;

        await sut.UpdateAsync(
            Channel,
            created.Id,
            new UpdateInboundWebhookRequest { Name = "renamed" }
        );
        db.InboundWebhookEndpoints.Single()
            .VerificationSecretEnvelope.Should()
            .Be("sealed:topsecret");

        await sut.UpdateAsync(
            Channel,
            created.Id,
            new UpdateInboundWebhookRequest { VerificationSecret = "rotated" }
        );
        db.InboundWebhookEndpoints.Single()
            .VerificationSecretEnvelope.Should()
            .Be("sealed:rotated");
        db.InboundWebhookEndpoints.Single().Name.Should().Be("renamed");
    }

    [Fact]
    public async Task Get_surfaces_the_generic_adapter_config_for_the_edit_form()
    {
        (InboundWebhookEndpointService sut, _, _, _) = Build();
        GenericInboundConfig config = new(
            SignatureHeaderName: "X-Signature",
            SignaturePrefix: "sha256=",
            SigningStringTemplate: "{timestamp}.{body}",
            TimestampHeaderName: "X-Timestamp",
            SharedSecretBodyField: null,
            EventKindJsonPath: "$.event",
            ProviderEventIdJsonPath: "$.id"
        );
        InboundWebhookEndpointDto created = (
            await sut.CreateAsync(
                Channel,
                Actor,
                new CreateInboundWebhookRequest
                {
                    Name = "zapier",
                    Adapter = WebhookAdapterKind.Generic,
                    VerificationSecret = "topsecret",
                    GenericConfig = config,
                }
            )
        ).Value;

        InboundWebhookEndpointDto fetched = (await sut.GetAsync(Channel, created.Id)).Value;

        fetched.Adapter.Should().Be(WebhookAdapterKind.Generic);
        fetched.GenericConfig.Should().NotBeNull();
        fetched.GenericConfig!.SignatureHeaderName.Should().Be("X-Signature");
        fetched.GenericConfig.SignaturePrefix.Should().Be("sha256=");
        fetched.GenericConfig.SigningStringTemplate.Should().Be("{timestamp}.{body}");
        fetched.GenericConfig.TimestampHeaderName.Should().Be("X-Timestamp");
        fetched.GenericConfig.SharedSecretBodyField.Should().BeNull();
        fetched.GenericConfig.EventKindJsonPath.Should().Be("$.event");
        fetched.GenericConfig.ProviderEventIdJsonPath.Should().Be("$.id");
    }

    [Fact]
    public async Task Get_leaves_the_generic_config_null_for_a_provider_adapter()
    {
        (InboundWebhookEndpointService sut, _, _, _) = Build();
        InboundWebhookEndpointDto created = (
            await sut.CreateAsync(Channel, Actor, Req(WebhookAdapterKind.Kofi))
        ).Value;

        InboundWebhookEndpointDto fetched = (await sut.GetAsync(Channel, created.Id)).Value;

        fetched.GenericConfig.Should().BeNull();
    }

    [Fact]
    public async Task Delete_soft_deletes_the_endpoint()
    {
        (InboundWebhookEndpointService sut, _, _, RecordingEventBus bus) = Build();
        InboundWebhookEndpointDto created = (await sut.CreateAsync(Channel, Actor, Req())).Value;
        bus.Published.Clear();

        (await sut.DeleteAsync(Channel, created.Id)).IsSuccess.Should().BeTrue();

        (await sut.ListAsync(Channel, new PaginationParams(1, 25, null, null)))
            .Value.Items.Should()
            .BeEmpty();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e => e.Domain == "webhooks" && e.Action == "deleted");
    }

    [Fact]
    public async Task Delete_of_an_unknown_endpoint_publishes_nothing()
    {
        (InboundWebhookEndpointService sut, _, _, RecordingEventBus bus) = Build();

        Result result = await sut.DeleteAsync(Channel, Guid.CreateVersion7());

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }
}
