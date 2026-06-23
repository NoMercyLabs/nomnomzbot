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
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Condition: check if the triggering user has at least a given role, on the unified authorization ladder
/// (viewer &lt; subscriber &lt; vip &lt; artist &lt; moderator &lt; lead_moderator &lt; editor &lt; broadcaster).
/// Usage: { "type": "user_role", "min_role": "moderator" }
///        { "type": "user_role", "role": "lead_moderator" }
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

    // Compare on the canonical ladder value so lead_moderator/editor/artist all rank correctly (the same parser the chat
    // command gate uses), rather than an ad-hoc local ordering.
    private static int RoleLevel(string role) => ChatRole.Parse(role).ToLevelValue();
}
