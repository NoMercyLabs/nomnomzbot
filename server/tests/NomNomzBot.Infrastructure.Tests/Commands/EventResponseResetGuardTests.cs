// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// On 2026-10-17 about ten event responses were reset in two minutes and the follow response lost its
/// pipeline, which broke follow TTS live. A reset of a response that is bound to a pipeline, command or
/// action now needs an explicit confirmation. These tests prove the row keeps its binding when the
/// confirmation is missing, that the refusal names what would be lost, and that a person can still reset.
/// </summary>
public sealed class EventResponseResetGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-3333-7000-8000-000000000001");
    private static readonly Guid FollowPipelineId = Guid.Parse(
        "019f4b00-3333-7000-8000-0000000000a1"
    );

    private static (
        EventResponseService Service,
        SupporterTestDbContext Db,
        IEventBus Bus
    ) BuildWithBoundFollowResponse()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "reset-guard-channel",
                NameNormalized = "reset-guard-channel",
            }
        );
        db.Pipelines.Add(
            new Pipeline
            {
                Id = FollowPipelineId,
                BroadcasterId = Tenant,
                Name = "Follow TTS",
            }
        );
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.Parse("019f4b00-3333-7000-8000-0000000000b1"),
                BroadcasterId = Tenant,
                EventType = "channel.follow",
                ResponseType = "pipeline",
                IsEnabled = true,
                PipelineId = FollowPipelineId,
            }
        );
        db.SaveChanges();

        IEventBus bus = Substitute.For<IEventBus>();
        return (new EventResponseService(db, bus, new TemplateHelperValidator()), db, bus);
    }

    [Fact]
    public async Task Reset_of_a_response_bound_to_a_pipeline_is_refused_without_confirm_and_keeps_the_binding()
    {
        (EventResponseService service, SupporterTestDbContext db, IEventBus bus) =
            BuildWithBoundFollowResponse();

        Result result = await service.ResetToDefaultAsync(Tenant.ToString(), "channel.follow");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CONFIRMATION_REQUIRED");
        result
            .ErrorMessage.Should()
            .Contain("Follow TTS", "the refusal names the pipeline that would be lost");

        EventResponse row = await db
            .EventResponses.AsNoTracking()
            .SingleAsync(r => r.EventType == "channel.follow");
        row.PipelineId.Should().Be(FollowPipelineId);
        row.IsEnabled.Should().BeTrue("a refused reset changes nothing");
        row.ResponseType.Should().Be("pipeline");
        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_of_a_bound_response_with_confirm_clears_the_binding_and_publishes_the_change()
    {
        (EventResponseService service, SupporterTestDbContext db, IEventBus bus) =
            BuildWithBoundFollowResponse();

        Result result = await service.ResetToDefaultAsync(
            Tenant.ToString(),
            "channel.follow",
            confirmed: true
        );

        result.IsSuccess.Should().BeTrue();
        EventResponse row = await db
            .EventResponses.AsNoTracking()
            .SingleAsync(r => r.EventType == "channel.follow");
        row.PipelineId.Should().BeNull();
        row.IsEnabled.Should().BeFalse();
        row.ResponseType.Should().Be("chat_message");
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.Domain == "event-responses"
                    && e.Action == "reset"
                    && e.BroadcasterId == Tenant
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Reset_of_a_response_bound_to_an_action_is_refused_and_names_the_action()
    {
        (EventResponseService service, SupporterTestDbContext db, IEventBus _) =
            BuildWithBoundFollowResponse();
        EventResponse row = await db.EventResponses.SingleAsync();
        row.SetBindingTarget(new(BindingTargetKind.Action, ActionType: "play_sound"));
        await db.SaveChangesAsync();

        Result result = await service.ResetToDefaultAsync(Tenant.ToString(), "channel.follow");

        result.ErrorCode.Should().Be("CONFIRMATION_REQUIRED");
        result.ErrorMessage.Should().Contain("play_sound");
        EventResponse kept = await db
            .EventResponses.AsNoTracking()
            .SingleAsync(r => r.EventType == "channel.follow");
        kept.TargetKind.Should().Be(BindingTargetKind.Action);
        kept.TargetActionType.Should().Be("play_sound");
    }
}
