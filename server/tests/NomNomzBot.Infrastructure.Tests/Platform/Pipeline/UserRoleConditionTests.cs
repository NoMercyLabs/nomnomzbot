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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

public class UserRoleConditionTests
{
    private static PipelineExecutionContext BuildCtx(string role)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = "chan",
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };
        ctx.Variables["user.role"] = role;
        return ctx;
    }

    private static ConditionDefinition MakeCond(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ConditionDefinition>(json)!;

    [Theory]
    [InlineData("broadcaster", "broadcaster", true)]
    [InlineData("broadcaster", "moderator", true)]
    [InlineData("broadcaster", "vip", true)]
    [InlineData("broadcaster", "subscriber", true)]
    [InlineData("broadcaster", "viewer", true)]
    public async Task Evaluate_BroadcasterMeetsAllRoles(
        string userRole,
        string minRole,
        bool expected
    )
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx(userRole);
        ConditionDefinition def = MakeCond(
            $$$"""{"type":"user_role","min_role":"{{{minRole}}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("moderator", "broadcaster", false)]
    [InlineData("moderator", "moderator", true)]
    [InlineData("moderator", "vip", true)]
    [InlineData("moderator", "subscriber", true)]
    [InlineData("moderator", "viewer", true)]
    public async Task Evaluate_ModeratorMeetsModeratorAndBelow(
        string userRole,
        string minRole,
        bool expected
    )
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx(userRole);
        ConditionDefinition def = MakeCond(
            $$$"""{"type":"user_role","min_role":"{{{minRole}}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("viewer", "broadcaster", false)]
    [InlineData("viewer", "moderator", false)]
    [InlineData("viewer", "vip", false)]
    [InlineData("viewer", "subscriber", false)]
    [InlineData("viewer", "viewer", true)]
    public async Task Evaluate_ViewerOnlyMeetsViewer(string userRole, string minRole, bool expected)
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx(userRole);
        ConditionDefinition def = MakeCond(
            $$$"""{"type":"user_role","min_role":"{{{minRole}}}"}"""
        );

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task Evaluate_ModAlias_Accepted()
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx("moderator");
        ConditionDefinition def = MakeCond("""{"type":"user_role","min_role":"mod"}""");

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_SubAlias_Accepted()
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx("subscriber");
        ConditionDefinition def = MakeCond("""{"type":"user_role","min_role":"sub"}""");

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_NoRoleVariable_DefaultsToViewer()
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = "chan",
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };
        // No user.role variable — defaults to "viewer"
        ConditionDefinition def = MakeCond("""{"type":"user_role","min_role":"moderator"}""");

        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Evaluate_NoParameters_DefaultsToViewer()
    {
        UserRoleCondition condition = new();
        PipelineExecutionContext ctx = BuildCtx("broadcaster");
        ConditionDefinition def = MakeCond("""{"type":"user_role"}"""); // no min_role param

        // Should default min_role to "viewer", any user meets that
        bool result = await condition.EvaluateAsync(ctx, def);
        result.Should().BeTrue();
    }

    [Fact]
    public void ConditionType_IsUserRole()
    {
        UserRoleCondition condition = new();
        condition.ConditionType.Should().Be("user_role");
    }
}
