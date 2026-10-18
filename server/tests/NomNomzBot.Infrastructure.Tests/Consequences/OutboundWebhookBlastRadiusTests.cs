// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// Proves the outbound webhook blast radius counts REAL rows from REAL SQL: the pipeline steps that
/// <c>send_webhook</c> to the endpoint (by raw id or by the owned-id form the dashboard stores), the delivery
/// history and the deliveries still waiting to go out. Every count test seeds rows owned by another channel or
/// another endpoint that must never appear, and a missing endpoint is a FAILURE, never a zero.
/// </summary>
public class OutboundWebhookBlastRadiusTests
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherChannel = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static OutboundWebhookEndpoint NewEndpoint(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
            Fqdn = "hooks.example.com",
            SigningSecretEnvelope = "sealed",
            IsEnabled = true,
        };

    private static OutboundWebhookDelivery NewDelivery(
        Guid broadcaster,
        Guid endpointId,
        WebhookDeliveryStatus status,
        DateTime? nextRetryAt = null
    ) =>
        new()
        {
            BroadcasterId = broadcaster,
            EndpointId = endpointId,
            WebhookMessageId = Guid.CreateVersion7(),
            EventType = "stream.online",
            RenderedBody = "{}",
            Attempt = 1,
            Status = status,
            NextRetryAt = nextRetryAt,
            CreatedAt = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc),
        };

    private static PipelineStep NewSendStep(Guid broadcaster, Guid pipelineId, string endpoint) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            PipelineId = pipelineId,
            ActionType = "send_webhook",
            ConfigJson = $$"""{"endpoint":"{{endpoint}}"}""",
            Order = 0,
        };

    private static Pipeline NewPipeline(Guid broadcaster, string name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Name = name,
        };

    private static NomNomzBot.Domain.Identity.Entities.Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.CreateVersion7(),
            TwitchChannelId = id.ToString(),
            Name = name,
            NameNormalized = name,
            OverlayToken = name + "-token",
        };

    private static OutboundWebhookEndpointService NewService(BlastRadiusTestDbContext db) =>
        new(
            db,
            Substitute.For<ITokenProtector>(),
            Substitute.For<ISubjectKeyService>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero)),
            Substitute.For<IEventBus>(),
            new TemplateHelperValidator(),
            Substitute.For<IOutboundWebhookDispatcher>(),
            new PipelineStepReferenceScanner(db)
        );

    [Fact]
    public async Task Preview_counts_the_steps_that_send_to_the_endpoint_by_raw_id_and_owned_id()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        OutboundWebhookEndpoint endpoint = NewEndpoint(Channel, "Discord relay");
        OutboundWebhookEndpoint sibling = NewEndpoint(Channel, "Other relay");

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            seed.Channels.AddRange(NewChannel(Channel, "streamer"), NewChannel(OtherChannel, "x"));
            Pipeline onFollow = NewPipeline(Channel, "On follow");
            Pipeline foreign = NewPipeline(OtherChannel, "Not mine");
            seed.Pipelines.AddRange(onFollow, foreign);
            seed.OutboundWebhookEndpoints.AddRange(endpoint, sibling);
            seed.PipelineSteps.AddRange(
                NewSendStep(Channel, onFollow.Id, endpoint.Id.ToString()),
                NewSendStep(Channel, onFollow.Id, OwnedIdCodec.Encode(endpoint.Id)),
                NewSendStep(Channel, onFollow.Id, OwnedIdCodec.Encode(sibling.Id)),
                NewSendStep(OtherChannel, foreign.Id, endpoint.Id.ToString())
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewService(db)
            .GetBlastRadiusAsync(Channel, endpoint.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        BlastRadiusCategoryDto steps = Assert.Single(result.Value.Categories);
        Assert.Equal(BlastRadiusCategoryKeys.PipelineSteps, steps.CategoryKey);
        Assert.Equal(2, steps.Count);
        Assert.Equal(["On follow"], steps.Sample);
        Assert.False(result.Value.IsMinimum);
    }

    [Fact]
    public async Task Preview_counts_the_delivery_history_and_the_deliveries_still_waiting()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        OutboundWebhookEndpoint endpoint = NewEndpoint(Channel, "Discord relay");
        OutboundWebhookEndpoint sibling = NewEndpoint(Channel, "Other relay");
        DateTime later = new(2026, 10, 18, 13, 0, 0, DateTimeKind.Utc);

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            seed.Channels.Add(NewChannel(Channel, "streamer"));
            seed.OutboundWebhookEndpoints.AddRange(endpoint, sibling);
            seed.OutboundWebhookDeliveries.AddRange(
                NewDelivery(Channel, endpoint.Id, WebhookDeliveryStatus.Delivered),
                NewDelivery(Channel, endpoint.Id, WebhookDeliveryStatus.DeadLetter),
                NewDelivery(Channel, endpoint.Id, WebhookDeliveryStatus.Pending),
                NewDelivery(Channel, endpoint.Id, WebhookDeliveryStatus.Failed, later),
                NewDelivery(Channel, endpoint.Id, WebhookDeliveryStatus.Failed),
                NewDelivery(Channel, sibling.Id, WebhookDeliveryStatus.Pending)
            );
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewService(db)
            .GetBlastRadiusAsync(Channel, endpoint.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Categories.Count);
        BlastRadiusCategoryDto history = Assert.Single(
            result.Value.Categories,
            category => category.CategoryKey == BlastRadiusCategoryKeys.WebhookDeliveries
        );
        BlastRadiusCategoryDto pending = Assert.Single(
            result.Value.Categories,
            category => category.CategoryKey == BlastRadiusCategoryKeys.PendingWebhookDeliveries
        );
        Assert.Equal(5, history.Count);
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task Preview_of_an_unused_endpoint_is_an_honest_zero()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        OutboundWebhookEndpoint endpoint = NewEndpoint(Channel, "Fresh");

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            seed.Channels.Add(NewChannel(Channel, "streamer"));
            seed.OutboundWebhookEndpoints.Add(endpoint);
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        Result<BlastRadiusDto> result = await NewService(db)
            .GetBlastRadiusAsync(Channel, endpoint.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Categories);
        Assert.Equal(0, result.Value.TotalReferences);
    }

    [Fact]
    public async Task Preview_fails_rather_than_reporting_zero_for_another_channels_or_a_deleted_endpoint()
    {
        using BlastRadiusSqliteTestDatabase database = BlastRadiusSqliteTestDatabase.Open();
        OutboundWebhookEndpoint foreign = NewEndpoint(OtherChannel, "Theirs");
        OutboundWebhookEndpoint deleted = NewEndpoint(Channel, "Gone");
        deleted.DeletedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (BlastRadiusTestDbContext seed = database.NewContext())
        {
            seed.Channels.AddRange(NewChannel(Channel, "streamer"), NewChannel(OtherChannel, "x"));
            seed.OutboundWebhookEndpoints.AddRange(foreign, deleted);
            await seed.SaveChangesAsync();
        }

        await using BlastRadiusTestDbContext db = database.NewContext();
        OutboundWebhookEndpointService service = NewService(db);
        Result<BlastRadiusDto> otherChannel = await service.GetBlastRadiusAsync(
            Channel,
            foreign.Id,
            CancellationToken.None
        );
        Result<BlastRadiusDto> softDeleted = await service.GetBlastRadiusAsync(
            Channel,
            deleted.Id,
            CancellationToken.None
        );

        Assert.True(otherChannel.IsFailure);
        Assert.Equal("NOT_FOUND", otherChannel.ErrorCode);
        Assert.True(softDeleted.IsFailure);
        Assert.Equal("NOT_FOUND", softDeleted.ErrorCode);
    }
}
