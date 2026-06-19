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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Condition: check if the triggering user has at least a given role.
/// Roles (ascending): viewer, subscriber, vip, moderator, broadcaster
/// Usage: { "type": "user_role", "min_role": "moderator" }
///        { "type": "user_role", "role": "broadcaster" }
/// </summary>
public sealed class UserRoleCondition : ICommandCondition
{
    public string ConditionType => "user_role";

    public bool Evaluate(PipelineExecutionContext ctx, ConditionDefinition condition)
    {
        string requiredRole = condition.Parameters is not null
            ? (GetParam(condition, "min_role") ?? GetParam(condition, "role") ?? "viewer")
            : "viewer";

        string userRole = ctx.Variables.GetValueOrDefault("user.role", "viewer");
        return RoleLevel(userRole) >= RoleLevel(requiredRole);
    }

    private static string? GetParam(ConditionDefinition c, string key)
    {
        if (c.Parameters is null)
            return null;
        if (!c.Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind == System.Text.Json.JsonValueKind.String ? elem.GetString() : null;
    }

    private static int RoleLevel(string role) =>
        role.ToLowerInvariant() switch
        {
            "broadcaster" => 5,
            "moderator" or "mod" => 4,
            "vip" => 3,
            "subscriber" or "sub" => 2,
            _ => 1, // viewer / everyone
        };
}
