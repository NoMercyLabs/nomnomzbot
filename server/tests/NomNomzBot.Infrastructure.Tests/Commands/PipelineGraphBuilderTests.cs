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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// <see cref="PipelineGraphBuilder"/> is the ONE translation from <see cref="PipelineStep"/> DB rows to
/// the flat wire graph <c>ChannelRegistry</c> caches as <c>Command.PipelineGraphJson</c> — the shape the
/// hot chat-command path actually executes (never by <c>PipelineId</c>/tree walk). Confirmed live
/// 2026-09-01: <see cref="PipelineStep.ContinueOnError"/> existed nowhere in the emitted graph, so no
/// row-backed pipeline could ever express "a failure here must not abort the run" for real chat
/// execution — the ONLY thing that worked was a hand-authored legacy <c>GraphJsonCache</c> blob that
/// happened to contain a raw <c>continue_on_error</c> key no seeder or editor could ever reproduce.
/// </summary>
public sealed class PipelineGraphBuilderTests
{
    private static PipelineStep Leaf(string actionType, bool continueOnError = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineId = Guid.NewGuid(),
            BroadcasterId = Guid.NewGuid(),
            ActionType = actionType,
            ConfigJson = "{}",
            ContinueOnError = continueOnError,
            Order = 0,
            IsEnabled = true,
        };

    [Fact]
    public void A_step_with_ContinueOnError_true_emits_continue_on_error_true_in_the_graph()
    {
        JsonElement graph = PipelineGraphBuilder.BuildGraph([
            Leaf("obs_switch_scene", continueOnError: true),
        ]);

        JsonElement step = graph.GetProperty("steps")[0];
        step.GetProperty("continue_on_error").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void A_step_with_ContinueOnError_false_emits_continue_on_error_false_in_the_graph()
    {
        JsonElement graph = PipelineGraphBuilder.BuildGraph([Leaf("send_message")]);

        JsonElement step = graph.GetProperty("steps")[0];
        step.GetProperty("continue_on_error").GetBoolean().Should().BeFalse();
    }
}
