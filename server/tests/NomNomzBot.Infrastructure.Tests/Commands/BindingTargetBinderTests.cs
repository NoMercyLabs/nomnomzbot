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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The one validator and applier every binding surface uses: a command or pipeline target must live in the
/// SAME channel, an action target needs its type, and a pipeline target equal to the surface's own pipeline
/// leaves no explicit target behind.
/// </summary>
public sealed class BindingTargetBinderTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e102");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000e103");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000e104");
    private static readonly Guid ForeignCommandId = Guid.Parse(
        "0192a000-0000-7000-8000-00000000e105"
    );
    private static readonly Guid ForeignPipelineId = Guid.Parse(
        "0192a000-0000-7000-8000-00000000e106"
    );

    private static async Task<AuthDbContext> SeededAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Commands.Add(NewCommand(CommandId, Channel, "mine"));
        db.Commands.Add(NewCommand(ForeignCommandId, OtherChannel, "theirs"));
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "mine",
                TriggerKind = "reward",
            }
        );
        db.Pipelines.Add(
            new()
            {
                Id = ForeignPipelineId,
                BroadcasterId = OtherChannel,
                Name = "theirs",
                TriggerKind = "reward",
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static Command NewCommand(Guid id, Guid channel, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = channel,
            Name = name,
            NameNormalized = name,
            Tier = "template",
            TemplateResponse = "hi",
        };

    [Fact]
    public async Task Validate_null_target_succeeds()
    {
        AuthDbContext db = await SeededAsync();

        Result result = await BindingTargetBinder.ValidateAsync(db, Channel, null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_command_of_this_channel_succeeds()
    {
        AuthDbContext db = await SeededAsync();

        Result result = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Command, CommandId.ToString())
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("malformed")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task Validate_command_outside_channel_or_malformed_fails_with_VALIDATION_FAILED(
        string scenario
    )
    {
        AuthDbContext db = await SeededAsync();
        string? id = scenario switch
        {
            "foreign" => ForeignCommandId.ToString(),
            "malformed" => "not-a-guid",
            "missing" => Guid.NewGuid().ToString(),
            _ => null,
        };

        Result result = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Command, id)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Be("That command does not exist in this channel.");
    }

    [Fact]
    public async Task Validate_pipeline_of_this_channel_succeeds_and_foreign_fails()
    {
        AuthDbContext db = await SeededAsync();

        Result own = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Pipeline, PipelineId.ToString())
        );
        Result foreign = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Pipeline, ForeignPipelineId.ToString())
        );

        own.IsSuccess.Should().BeTrue();
        foreign.IsFailure.Should().BeTrue();
        foreign.ErrorMessage.Should().Be("That pipeline does not exist in this channel.");
    }

    [Fact]
    public async Task Validate_pipeline_without_id_succeeds_but_malformed_id_fails()
    {
        AuthDbContext db = await SeededAsync();

        Result none = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Pipeline)
        );
        Result bad = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Pipeline, "nope")
        );

        none.IsSuccess.Should().BeTrue();
        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Validate_action_needs_an_action_type()
    {
        AuthDbContext db = await SeededAsync();

        Result named = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Action, ActionType: "play_sound")
        );
        Result blank = await BindingTargetBinder.ValidateAsync(
            db,
            Channel,
            new(BindingTargetKind.Action, ActionType: " ")
        );

        named.IsSuccess.Should().BeTrue();
        blank.IsFailure.Should().BeTrue();
        blank.ErrorCode.Should().Be("VALIDATION_FAILED");
        blank.ErrorMessage.Should().Be("An action target needs an action type.");
    }

    [Fact]
    public void Apply_command_target_writes_the_command_and_clears_the_pipeline()
    {
        Reward reward = new() { PipelineId = PipelineId };

        BindingTargetBinder.ApplyTo(
            reward,
            new(BindingTargetKind.Command, CommandId.ToString()),
            PipelineId
        );

        reward.TargetKind.Should().Be(BindingTargetKind.Command);
        reward.TargetCommandId.Should().Be(CommandId);
        reward.PipelineId.Should().BeNull();
    }

    [Fact]
    public void Apply_action_target_stores_type_and_params()
    {
        Reward reward = new();
        Dictionary<string, JsonElement> args = new()
        {
            ["clip"] = JsonSerializer.SerializeToElement("airhorn"),
        };

        BindingTargetBinder.ApplyTo(
            reward,
            new(BindingTargetKind.Action, ActionType: "play_sound", Params: args),
            null
        );

        reward.TargetKind.Should().Be(BindingTargetKind.Action);
        reward.TargetActionType.Should().Be("play_sound");
        reward.TargetActionParamsJson.Should().Be("""{"clip":"airhorn"}""");
    }

    [Fact]
    public void Apply_pipeline_target_equal_to_existing_pipeline_clears_the_explicit_target()
    {
        Reward reward = new()
        {
            TargetKind = BindingTargetKind.Command,
            TargetCommandId = CommandId,
            PipelineId = PipelineId,
        };

        BindingTargetBinder.ApplyTo(
            reward,
            new(BindingTargetKind.Pipeline, PipelineId.ToString()),
            PipelineId
        );

        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        reward.TargetCommandId.Should().BeNull();
        reward.TargetActionType.Should().BeNull();
        reward.PipelineId.Should().Be(PipelineId);
    }

    [Fact]
    public void Apply_pipeline_target_that_differs_binds_that_pipeline()
    {
        Guid other = Guid.NewGuid();
        Reward reward = new() { PipelineId = PipelineId };

        BindingTargetBinder.ApplyTo(
            reward,
            new(BindingTargetKind.Pipeline, other.ToString()),
            PipelineId
        );

        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
        reward.PipelineId.Should().Be(other);
    }

    [Fact]
    public void Apply_pipeline_target_without_id_means_no_binding()
    {
        Reward reward = new() { PipelineId = PipelineId };

        BindingTargetBinder.ApplyTo(reward, new(BindingTargetKind.Pipeline), PipelineId);

        reward.GetBindingTarget().Should().BeNull();
        reward.PipelineId.Should().BeNull();
    }

    [Fact]
    public void Apply_null_target_leaves_the_row_unchanged()
    {
        Reward reward = new() { PipelineId = PipelineId };

        BindingTargetBinder.ApplyTo(reward, null, PipelineId);

        reward.PipelineId.Should().Be(PipelineId);
        reward.TargetKind.Should().Be(BindingTargetKind.Pipeline);
    }
}
