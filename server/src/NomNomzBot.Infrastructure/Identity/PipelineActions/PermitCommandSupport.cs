// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity.PipelineActions;

/// <summary>
/// Shared chat-context resolution and Gate-2 enforcement for the <c>permit</c> / <c>unpermit</c> pipeline actions.
/// The chat/pipeline path has no <c>[RequireAction]</c> attribute stack, so the <c>permit:issue</c> capability is
/// checked here (a level that clears it OR a direct grant), and the invoker / target Twitch ids are resolved to
/// platform user Guids — the key <see cref="IPermitService"/> operates on — via <see cref="IUserService"/>.
/// </summary>
internal static class PermitCommandSupport
{
    private const string PermitIssueActionKey = "permit:issue";

    /// <summary>
    /// Resolve the invoking user and require the <c>permit:issue</c> capability. Returns the invoker's user Guid on
    /// success, or a <c>FORBIDDEN</c> failure when they may not manage permits.
    /// </summary>
    public static async Task<Result<Guid>> AuthorizeInvokerAsync(
        IUserService users,
        IRoleResolver roles,
        PipelineExecutionContext ctx,
        string verb
    )
    {
        Result<Guid> invoker = await ResolveUserAsync(
            users,
            ctx.TriggeredByUserId,
            ctx.Variables.GetValueOrDefault("user.name") ?? ctx.TriggeredByDisplayName,
            ctx.TriggeredByDisplayName,
            $"{verb}: the invoking user could not be resolved",
            ctx.CancellationToken
        );
        if (invoker.IsFailure)
            return invoker;

        Result<bool> mayIssue = await roles.HasCapabilityAsync(
            invoker.Value,
            ctx.BroadcasterId,
            PermitIssueActionKey,
            ctx.CancellationToken
        );
        return mayIssue.IsSuccess && mayIssue.Value
            ? invoker
            : Result.Failure<Guid>(
                $"{verb}: you are not allowed to manage permits (needs {PermitIssueActionKey})",
                "FORBIDDEN"
            );
    }

    /// <summary>
    /// Resolve the target user's Twitch id (from the configured <c>target_variable</c>, default <c>target.id</c>) to
    /// a platform user Guid. Fails closed when no target variable is present.
    /// </summary>
    public static Task<Result<Guid>> ResolveTargetAsync(
        IUserService users,
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string verb
    )
    {
        string targetVariable = action.GetString("target_variable") ?? "target.id";
        string? targetTwitchId = ctx.Variables.GetValueOrDefault(targetVariable);
        if (string.IsNullOrWhiteSpace(targetTwitchId))
            return Task.FromResult(
                Result.Failure<Guid>(
                    $"{verb}: no target — mention a user with @name",
                    "VALIDATION_FAILED"
                )
            );

        string login = ctx.Variables.GetValueOrDefault("target.name") ?? targetTwitchId;
        string display = ctx.Variables.GetValueOrDefault("target.display") ?? login;
        return ResolveUserAsync(
            users,
            targetTwitchId,
            login,
            display,
            $"{verb}: the target user could not be resolved",
            ctx.CancellationToken
        );
    }

    /// <summary>The name echoed back for the target in a confirmation message (display name, then login).</summary>
    public static string TargetLabel(PipelineExecutionContext ctx) =>
        ctx.Variables.GetValueOrDefault("target.display")
        ?? ctx.Variables.GetValueOrDefault("target")
        ?? "the user";

    private static async Task<Result<Guid>> ResolveUserAsync(
        IUserService users,
        string platformUserId,
        string username,
        string displayName,
        string failureMessage,
        CancellationToken ct
    )
    {
        Result<UserDto> user = await users.GetOrCreateAsync(
            platformUserId,
            username,
            displayName,
            ct
        );
        if (user.IsFailure)
            return Result.Failure<Guid>(user.ErrorMessage ?? failureMessage, user.ErrorCode);
        return Guid.TryParse(user.Value.Id, out Guid id)
            ? Result.Success(id)
            : Result.Failure<Guid>(failureMessage, "VALIDATION_FAILED");
    }
}
