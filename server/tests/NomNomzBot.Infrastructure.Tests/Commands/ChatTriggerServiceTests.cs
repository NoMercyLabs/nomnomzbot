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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the chat-trigger CRUD honesty checks: a trigger must be able to DO something (template or
/// pipeline) and a regex must compile at write time; every successful write refreshes the hot-path
/// cache via the registry (so the change is live without a restart); cooldown/floor inputs are clamped.
/// </summary>
public sealed class ChatTriggerServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f6e00-6666-7000-8000-000000000001");

    private static (ChatTriggerService Service, AuthDbContext Db, IChannelRegistry Registry) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        return (new(db, registry), db, registry);
    }

    [Fact]
    public async Task Create_persists_the_trigger_and_refreshes_the_hot_path_cache()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new() { Pattern = "good bot", Response = "thanks {user}!" }
        );

        created.IsSuccess.Should().BeTrue();
        created.Value.MatchType.Should().Be("contains");
        created.Value.CooldownSeconds.Should().Be(30, "the spam-guard default");
        (await db.ChatTriggers.CountAsync()).Should().Be(1);
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_trigger_with_nothing_to_do_is_rejected()
    {
        (ChatTriggerService service, _, IChannelRegistry registry) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new() { Pattern = "hello" } // no response, no pipeline
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        await registry
            .DidNotReceiveWithAnyArgs()
            .InvalidateChatTriggersAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_uncompilable_regex_is_rejected_at_write_time()
    {
        (ChatTriggerService service, _, _) = Build();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new()
            {
                Pattern = "([unclosed",
                MatchType = "regex",
                Response = "hi",
            }
        );

        created.IsFailure.Should().BeTrue("an invalid regex would silently never fire");
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Update_is_partial_and_revalidates_the_combined_state()
    {
        (ChatTriggerService service, _, IChannelRegistry registry) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new() { Pattern = "hello", Response = "hi!" }
            )
        )
            .Value
            .Id;
        registry.ClearReceivedCalls();

        Result<ChatTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            id,
            new() { CooldownSeconds = 999_999, IsEnabled = false }
        );

        updated.Value.Pattern.Should().Be("hello", "absent fields stay unchanged");
        updated.Value.CooldownSeconds.Should().Be(86_400, "clamped to the 24h ceiling");
        updated.Value.IsEnabled.Should().BeFalse();
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_clears_a_bound_pipeline_via_the_empty_guid_sentinel()
    {
        (ChatTriggerService service, AuthDbContext db, _) = Build();
        Guid pipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Tenant,
                Name = "combo pipeline",
                TriggerKind = "chat_trigger",
            }
        );
        await db.SaveChangesAsync();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new() { Pattern = "combo", PipelineId = pipelineId }
            )
        )
            .Value
            .Id;

        // Switch the trigger back to a text response. The client sends Guid.Empty to CLEAR the pipeline — a null
        // pipelineId is dropped by its explicitNulls=false serializer and would leave the old binding, so the
        // pipeline would keep firing instead of the reply (PipelineId wins over Response on the domain entity).
        Result<ChatTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            id,
            new() { PipelineId = Guid.Empty, Response = "back to text" }
        );

        updated.IsSuccess.Should().BeTrue();
        updated.Value.PipelineId.Should().BeNull("the empty sentinel unbinds the pipeline");
        updated.Value.Response.Should().Be("back to text");
    }

    [Fact]
    public async Task Create_refuses_a_pipeline_id_from_another_channel()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();
        Guid otherChannel = Guid.NewGuid();
        Guid foreignPipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = foreignPipelineId,
                BroadcasterId = otherChannel,
                Name = "someone else's pipeline",
                TriggerKind = "chat_trigger",
            }
        );
        await db.SaveChangesAsync();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new() { Pattern = "steal", PipelineId = foreignPipelineId }
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("PIPELINE_NOT_IN_CHANNEL");
        (await db.ChatTriggers.CountAsync()).Should().Be(0);
        await registry
            .DidNotReceiveWithAnyArgs()
            .InvalidateChatTriggersAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_refuses_a_pipeline_id_from_another_channel()
    {
        (ChatTriggerService service, AuthDbContext db, _) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new() { Pattern = "hello", Response = "hi!" }
            )
        )
            .Value
            .Id;
        Guid otherChannel = Guid.NewGuid();
        Guid foreignPipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = foreignPipelineId,
                BroadcasterId = otherChannel,
                Name = "someone else's pipeline",
                TriggerKind = "chat_trigger",
            }
        );
        await db.SaveChangesAsync();

        Result<ChatTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            id,
            new() { PipelineId = foreignPipelineId }
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("PIPELINE_NOT_IN_CHANNEL");
        (await db.ChatTriggers.FirstAsync(t => t.Id == id)).PipelineId.Should().BeNull();
    }

    [Fact]
    public async Task An_overlay_line_duration_outside_the_allowed_range_is_refused_on_create_and_update()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();
        ReplyLook look = new(ReplyHighlight.Warning, ReplySize.Large, DurationSeconds: 15);
        ChatTriggerDto good = (
            await service.CreateAsync(
                Tenant.ToString(),
                new()
                {
                    Pattern = "choo",
                    Response = "choo choo",
                    ReplyDelivery = new(ReplyTarget.Overlay, Look: look),
                }
            )
        ).Value;
        ReplyDelivery bad = new(ReplyTarget.Overlay, Look: new ReplyLook(DurationSeconds: 121));
        registry.ClearReceivedCalls();

        Result<ChatTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new()
            {
                Pattern = "other",
                Response = "x",
                ReplyDelivery = bad,
            }
        );
        Result<ChatTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            good.Id,
            new() { ReplyDelivery = bad }
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChatTriggers.CountAsync()).Should().Be(1);
        // The test database does not map the column, so the tracked row is what shows an untouched look.
        db.ChatTriggers.Single()
            .ReplyDelivery.Should()
            .Be(new ReplyDelivery(ReplyTarget.Overlay, Look: look));
        await registry
            .DidNotReceiveWithAnyArgs()
            .InvalidateChatTriggersAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_removes_the_trigger_and_refreshes_the_cache()
    {
        (ChatTriggerService service, AuthDbContext db, IChannelRegistry registry) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new() { Pattern = "bye", Response = "cya" }
            )
        )
            .Value
            .Id;
        registry.ClearReceivedCalls();

        Result deleted = await service.DeleteAsync(Tenant.ToString(), id);

        deleted.IsSuccess.Should().BeTrue();
        (await db.ChatTriggers.IgnoreQueryFilters().CountAsync(t => t.DeletedAt == null))
            .Should()
            .Be(0);
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Tenant, Arg.Any<CancellationToken>());
    }
}
