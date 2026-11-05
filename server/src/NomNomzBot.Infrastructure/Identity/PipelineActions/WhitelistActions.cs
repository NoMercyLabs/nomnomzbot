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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity.PipelineActions;

/// <summary>
/// Shared invoker gate for <see cref="WhitelistAction"/> / <see cref="UnwhitelistAction"/>: only the
/// broadcaster may grant or remove a manual community-standing override, mirroring the <c>!whitelist</c> /
/// <c>!unwhitelist</c> chat builtins (<see cref="WhitelistSupport.BroadcasterLevel"/>) rather than trusting
/// whatever floor the enclosing pipeline's trigger happens to carry.
/// </summary>
internal static class WhitelistCommandSupport
{
    public const int BroadcasterLevel = 40;

    public static readonly Dictionary<string, CommunityStanding> Levels = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["subscriber"] = CommunityStanding.Subscriber,
        ["vip"] = CommunityStanding.Vip,
        ["artist"] = CommunityStanding.Artist,
        ["moderator"] = CommunityStanding.Moderator,
    };

    public static async Task<Result<Guid>> AuthorizeInvokerAsync(
        IUserService users,
        IRoleResolver roles,
        PipelineExecutionContext ctx,
        string verb
    )
    {
        Result<Application.Identity.Dtos.UserDto> invoker = await users.GetOrCreateAsync(
            ctx.TriggeredByUserId,
            ctx.Variables.GetValueOrDefault("user.name") ?? ctx.TriggeredByDisplayName,
            ctx.TriggeredByDisplayName,
            cancellationToken: ctx.CancellationToken
        );
        if (invoker.IsFailure || !Guid.TryParse(invoker.Value.Id, out Guid invokerId))
            return Result.Failure<Guid>($"{verb}: the invoking user could not be resolved");

        Result<int> level = await roles.ResolveEffectiveLevelAsync(
            invokerId,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        return level is { IsSuccess: true, Value: >= BroadcasterLevel }
            ? Result.Success(invokerId)
            : Result.Failure<Guid>($"{verb}: only the broadcaster may do this", "FORBIDDEN");
    }

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
        return ResolveUserAsync(users, targetTwitchId, login, display, verb, ctx.CancellationToken);
    }

    public static string TargetLabel(PipelineExecutionContext ctx) =>
        ctx.Variables.GetValueOrDefault("target.display")
        ?? ctx.Variables.GetValueOrDefault("target")
        ?? "the user";

    private static async Task<Result<Guid>> ResolveUserAsync(
        IUserService users,
        string platformUserId,
        string username,
        string displayName,
        string verb,
        CancellationToken ct
    )
    {
        Result<Application.Identity.Dtos.UserDto> user = await users.GetOrCreateAsync(
            platformUserId,
            username,
            displayName,
            cancellationToken: ct
        );
        if (user.IsFailure)
            return Result.Failure<Guid>(
                $"{verb}: the target user could not be resolved",
                user.ErrorCode
            );
        return Guid.TryParse(user.Value.Id, out Guid id)
            ? Result.Success(id)
            : Result.Failure<Guid>(
                $"{verb}: the target user could not be resolved",
                "VALIDATION_FAILED"
            );
    }
}

/// <summary>
/// Grants a viewer a manual community-standing override (the <c>!whitelist @user
/// subscriber|vip|moderator</c> capability). Broadcaster-only, enforced in-action since a pipeline has no
/// <c>[RequireAction]</c> floor of its own.
///
/// Parameters:
///   target_variable — the variable holding the target's Twitch id (default <c>target.id</c>).
///   standing — subscriber, vip, artist or moderator (required).
///
/// Usage example:
///   { "type": "whitelist", "standing": "vip" }
/// </summary>
public sealed class WhitelistAction : ICommandAction
{
    private readonly ICommunityStandingService _standings;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;

    public string ActionType => "whitelist";

    public LocalizedText Category => new("pipeline.category.identity");

    public LocalizedText Description => new("pipeline.whitelist.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "target_variable",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.whitelist.target_variable.help")
            ),
            new(
                "standing",
                PipelineActionFieldKind.Enum,
                Required: true,
                Options: ["subscriber", "vip", "artist", "moderator"],
                Description: new("pipeline.whitelist.standing.help")
            ),
        ];

    public WhitelistAction(
        ICommunityStandingService standings,
        IUserService users,
        IRoleResolver roles
    )
    {
        _standings = standings;
        _users = users;
        _roles = roles;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<Guid> invoker = await WhitelistCommandSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            ctx,
            "whitelist"
        );
        if (invoker.IsFailure)
            return ActionResult.Failure(invoker.ErrorMessage!);

        string levelName = action.GetString("standing") ?? string.Empty;
        if (!WhitelistCommandSupport.Levels.TryGetValue(levelName, out CommunityStanding standing))
            return ActionResult.Failure($"whitelist: unknown standing \"{levelName}\"");

        Result<Guid> target = await WhitelistCommandSupport.ResolveTargetAsync(
            _users,
            ctx,
            action,
            "whitelist"
        );
        if (target.IsFailure)
            return ActionResult.Failure(target.ErrorMessage!);

        Result saved = await _standings.SetManualStandingAsync(
            ctx.BroadcasterId,
            target.Value,
            standing,
            ctx.CancellationToken
        );
        return saved.IsSuccess
            ? ActionResult.Success(
                $"Granted {levelName} to {WhitelistCommandSupport.TargetLabel(ctx)}."
            )
            : ActionResult.Failure(saved.ErrorMessage ?? "whitelist: save failed");
    }
}

/// <summary>
/// Removes a viewer's manual community-standing override (the <c>!unwhitelist @user</c> capability).
/// Broadcaster-only, enforced in-action.
///
/// Parameters:
///   target_variable — the variable holding the target's Twitch id (default <c>target.id</c>).
///
/// Usage example:
///   { "type": "unwhitelist" }
/// </summary>
public sealed class UnwhitelistAction : ICommandAction
{
    private readonly ICommunityStandingService _standings;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;

    public string ActionType => "unwhitelist";

    public LocalizedText Category => new("pipeline.category.identity");

    public LocalizedText Description => new("pipeline.unwhitelist.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "target_variable",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.unwhitelist.target_variable.help")
            ),
        ];

    public UnwhitelistAction(
        ICommunityStandingService standings,
        IUserService users,
        IRoleResolver roles
    )
    {
        _standings = standings;
        _users = users;
        _roles = roles;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<Guid> invoker = await WhitelistCommandSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            ctx,
            "unwhitelist"
        );
        if (invoker.IsFailure)
            return ActionResult.Failure(invoker.ErrorMessage!);

        Result<Guid> target = await WhitelistCommandSupport.ResolveTargetAsync(
            _users,
            ctx,
            action,
            "unwhitelist"
        );
        if (target.IsFailure)
            return ActionResult.Failure(target.ErrorMessage!);

        Result<bool> removed = await _standings.RemoveManualStandingAsync(
            ctx.BroadcasterId,
            target.Value,
            ctx.CancellationToken
        );
        if (removed.IsFailure)
            return ActionResult.Failure(removed.ErrorMessage ?? "unwhitelist: remove failed");

        string label = WhitelistCommandSupport.TargetLabel(ctx);
        return ActionResult.Success(
            removed.Value ? $"Removed {label}'s override." : $"{label} had no override."
        );
    }
}
