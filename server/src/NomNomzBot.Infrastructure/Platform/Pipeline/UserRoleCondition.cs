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

    public Task<bool> EvaluateAsync(PipelineExecutionContext ctx, ConditionDefinition condition)
    {
        string requiredRole = condition.Parameters is not null
            ? (GetParam(condition, "min_role") ?? GetParam(condition, "role") ?? "viewer")
            : "viewer";

        string userRole = ctx.Variables.GetValueOrDefault("user.role", "viewer");
        return Task.FromResult(RoleLevel(userRole) >= RoleLevel(requiredRole));
    }

    private static string? GetParam(ConditionDefinition c, string key)
    {
        if (c.Parameters is null)
            return null;
        if (!c.Parameters.TryGetValue(key, out JsonElement elem))
            return null;
        return elem.ValueKind == JsonValueKind.String ? elem.GetString() : null;
    }

    // Compare on the canonical ladder value so lead_moderator/editor/artist all rank correctly (the same parser the chat
    // command gate uses), rather than an ad-hoc local ordering.
    private static int RoleLevel(string role) => ChatRole.Parse(role).ToLevelValue();
}
