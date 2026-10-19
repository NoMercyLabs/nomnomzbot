// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Rewards.Entities;

namespace NomNomzBot.Domain.Tests.Commands;

public sealed class BindingTargetTests
{
    private static readonly Guid PipelineGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid CommandGuid = Guid.Parse("0192a000-0000-7000-8000-0000000000a2");

    [Fact]
    public void A_row_without_target_columns_reads_as_its_legacy_pipeline()
    {
        Reward reward = new() { Title = "old", PipelineId = PipelineGuid };

        BindingTarget? target = reward.GetBindingTarget();

        target.Should().Be(new BindingTarget(BindingTargetKind.Pipeline, PipelineGuid.ToString()));
    }

    [Fact]
    public void A_command_target_is_stored_in_the_target_column_and_clears_the_pipeline()
    {
        Reward reward = new() { Title = "r", PipelineId = PipelineGuid };

        reward.SetBindingTarget(new(BindingTargetKind.Command, CommandGuid.ToString()));

        reward.TargetKind.Should().Be(BindingTargetKind.Command);
        reward.TargetCommandId.Should().Be(CommandGuid);
        reward.PipelineId.Should().BeNull("a row never holds two targets");
        reward
            .GetBindingTarget()
            .Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandGuid.ToString()));
    }

    [Fact]
    public void An_action_target_round_trips_its_type_and_params()
    {
        Reward reward = new() { Title = "r" };
        Dictionary<string, JsonElement> actionParams = new()
        {
            ["message"] = JsonDocument.Parse("\"hi\"").RootElement.Clone(),
        };

        reward.SetBindingTarget(
            new(BindingTargetKind.Action, ActionType: "send_message", Params: actionParams)
        );

        reward.TargetKind.Should().Be(BindingTargetKind.Action);
        reward.TargetActionType.Should().Be("send_message");
        BindingTarget? read = reward.GetBindingTarget();
        read!.ActionType.Should().Be("send_message");
        read.Params!["message"].GetString().Should().Be("hi");
    }

    [Fact]
    public void A_non_pipeline_scheduled_task_stores_an_empty_pipeline_id_and_reads_its_command()
    {
        ScheduledPipelineTask task = new() { PipelineId = PipelineGuid };

        task.SetBindingTarget(new(BindingTargetKind.Command, CommandGuid.ToString()));

        task.PipelineId.Should().Be(Guid.Empty, "the legacy column is non-null");
        task.GetBindingTarget()!.Kind.Should().Be(BindingTargetKind.Command);
    }

    [Fact]
    public void A_token_with_only_the_old_pipeline_list_still_allows_those_pipelines_as_targets()
    {
        AutomationApiToken token = new()
        {
            AllowedPipelineIdsJson = JsonSerializer.Serialize(new[] { PipelineGuid }),
        };

        IReadOnlyList<BindingTarget>? allowed = token.GetAllowedTargets();

        allowed
            .Should()
            .Equal(new BindingTarget(BindingTargetKind.Pipeline, PipelineGuid.ToString()));
    }

    [Fact]
    public void A_token_with_a_target_list_prefers_it_and_one_with_nothing_is_unrestricted()
    {
        AutomationApiToken pinned = new()
        {
            AllowedTargetsJson = BindingTargetJson.SerializeList([
                new(BindingTargetKind.Command, CommandGuid.ToString()),
            ]),
            AllowedPipelineIdsJson = JsonSerializer.Serialize(new[] { PipelineGuid }),
        };

        pinned
            .GetAllowedTargets()
            .Should()
            .Equal(new BindingTarget(BindingTargetKind.Command, CommandGuid.ToString()));
        new AutomationApiToken().GetAllowedTargets().Should().BeNull();
    }
}
