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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Answers a failed <see cref="RequireActionAttribute"/> with RFC-7807 problem details that say why:
/// <c>code: FORBIDDEN_ACTION</c>, the <c>action</c> key, the <c>requiredRole</c> the channel set for it and the
/// <c>heldRole</c> the caller resolves to. Roles are <see cref="PermissionLevel"/> names, never ladder numbers.
/// Every other authorization outcome (challenge, other forbids, success) goes to the framework default.
/// </summary>
public sealed class ForbiddenActionResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string Code = "FORBIDDEN_ACTION";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        ActionAuthorizationRequirement? failed = authorizeResult.Forbidden
            ? authorizeResult
                .AuthorizationFailure?.FailedRequirements.OfType<ActionAuthorizationRequirement>()
                .FirstOrDefault()
            : null;

        ForbiddenActionDetail? detail = failed is null
            ? null
            : await ResolveAsync(context, failed.ActionKey);

        if (detail is null)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        ProblemDetails problem = new()
        {
            Type = "https://nomnomz.bot/problems/forbidden-action",
            Title = "Forbidden",
            Status = StatusCodes.Status403Forbidden,
            Detail =
                $"'{detail.Action}' needs the {detail.RequiredRole} role in this channel; you hold {detail.HeldRole}.",
            Extensions =
            {
                ["code"] = Code,
                ["action"] = detail.Action,
                ["requiredRole"] = detail.RequiredRole,
                ["heldRole"] = detail.HeldRole,
            },
        };
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem),
            context.RequestAborted
        );
    }

    private static async Task<ForbiddenActionDetail?> ResolveAsync(
        HttpContext context,
        string actionKey
    )
    {
        IServiceProvider services = context.RequestServices;
        ICurrentUserService user = services.GetRequiredService<ICurrentUserService>();
        ICurrentTenantService tenant = services.GetRequiredService<ICurrentTenantService>();
        if (!Guid.TryParse(user.UserId, out Guid userId) || tenant.BroadcasterId is not { } channel)
            return null;
        if (channel == Guid.Empty)
            return null;

        Result<int> required = await services
            .GetRequiredService<IActionAuthorizationService>()
            .GetEffectiveLevelAsync(channel, actionKey, context.RequestAborted);
        Result<int> held = await services
            .GetRequiredService<IRoleResolver>()
            .ResolveEffectiveLevelAsync(userId, channel, context.RequestAborted);
        if (!required.IsSuccess)
            return null;

        return new(
            actionKey,
            AuthorizationLadder.FromLevelValue(required.Value).ToString(),
            AuthorizationLadder.FromLevelValue(held.IsSuccess ? held.Value : 0).ToString()
        );
    }

    private sealed record ForbiddenActionDetail(
        string Action,
        string RequiredRole,
        string HeldRole
    );
}
