// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.PipelineActions;

namespace NomNomzBot.Infrastructure.Identity.Builtins;

/// <summary>
/// The zero-config chat surface for temporary delegation (roles-permissions §3.6, BUILD item 24b):
/// <c>!permit @user &lt;role|capability&gt; [minutes]</c> and <c>!unpermit @user [role|capability]</c>
/// work out of the box — no hand-wired pipeline needed. The invoker is gated on <c>permit:issue</c>
/// exactly like the pipeline actions and the HTTP surface, and <c>IPermitService</c> re-asserts the
/// no-escalation + <c>IsGrantableViaPermit</c> guardrails, so this adds a surface, never a bypass.
/// The @mention carries only a NAME, so the target resolves login → id via Helix — Twitch channels
/// only for now (a non-Twitch chatter gets an honest "not found").
/// </summary>
internal static class PermitBuiltinSupport
{
    private const string PermitIssueActionKey = "permit:issue";

    /// <summary>Resolves the invoking chatter and requires the <c>permit:issue</c> capability.</summary>
    public static async Task<Result<Guid>> AuthorizeInvokerAsync(
        IUserService users,
        IRoleResolver roles,
        BuiltinCommandContext context,
        string verb,
        CancellationToken ct
    )
    {
        Result<UserDto> invoker = await users.GetOrCreateAsync(
            context.TriggeringUserId,
            context.TriggeringUserLogin,
            context.TriggeringUserDisplayName,
            cancellationToken: ct
        );
        if (invoker.IsFailure || !Guid.TryParse(invoker.Value.Id, out Guid invokerId))
            return Result.Failure<Guid>($"{verb}: your account could not be resolved", "NOT_FOUND");

        Result<bool> mayIssue = await roles.HasCapabilityAsync(
            invokerId,
            context.BroadcasterId,
            PermitIssueActionKey,
            ct
        );
        return mayIssue.IsSuccess && mayIssue.Value
            ? Result.Success(invokerId)
            : Result.Failure<Guid>(
                $"{verb}: you are not allowed to manage permits (needs {PermitIssueActionKey})",
                "FORBIDDEN"
            );
    }

    /// <summary>Resolves an @mention to the target's platform user Guid (Helix login → id → User).</summary>
    public static async Task<Result<(Guid UserId, string Label)>> ResolveTargetAsync(
        IUserService users,
        ITwitchUsersApi twitchUsers,
        string mention,
        string verb,
        CancellationToken ct
    )
    {
        string login = mention.Trim().TrimStart('@').ToLowerInvariant();
        if (login.Length == 0)
            return Result.Failure<(Guid, string)>(
                $"{verb}: no target — mention a user with @name",
                "VALIDATION_FAILED"
            );

        Result<IReadOnlyList<TwitchUser>> lookup = await twitchUsers.GetUsersByLoginsAsync(
            [login],
            ct
        );
        TwitchUser? twitchUser = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        if (twitchUser is null)
            return Result.Failure<(Guid, string)>(
                $"{verb}: '{login}' was not found on Twitch",
                "NOT_FOUND"
            );

        Result<UserDto> user = await users.GetOrCreateAsync(
            twitchUser.Id,
            twitchUser.Login,
            twitchUser.DisplayName,
            cancellationToken: ct
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid userId))
            return Result.Failure<(Guid, string)>(
                $"{verb}: the target user could not be resolved",
                "NOT_FOUND"
            );

        return Result.Success((userId, twitchUser.DisplayName));
    }
}

/// <summary>Chat builtin <c>!permit @user &lt;role|capability&gt; [minutes]</c> — grants a bounded delegation.</summary>
public sealed class PermitBuiltin : IBuiltinCommand
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly TimeProvider _clock;

    public PermitBuiltin(
        IPermitService permits,
        IUserService users,
        IRoleResolver roles,
        ITwitchUsersApi twitchUsers,
        TimeProvider clock
    )
    {
        _permits = permits;
        _users = users;
        _roles = roles;
        _twitchUsers = twitchUsers;
        _clock = clock;
    }

    public string BuiltinKey => "permit";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 10; // Moderator on the unified ladder; the real gate is permit:issue below.

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string[] args = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 2)
            return Result.Success("Usage: !permit @user <role|capability> [minutes]");

        Result<Guid> invoker = await PermitBuiltinSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            context,
            "permit",
            ct
        );
        if (invoker.IsFailure)
            return Result.Success(invoker.ErrorMessage!);

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            _users,
            _twitchUsers,
            args[0],
            "permit",
            ct
        );
        if (target.IsFailure)
            return Result.Success(target.ErrorMessage!);

        string token = args[1].Trim();
        DateTime? expiresAt =
            args.Length >= 3 && int.TryParse(args[2], out int minutes) && minutes > 0
                ? _clock.GetUtcNow().UtcDateTime.AddMinutes(minutes)
                : null;

        // A token that names a management role is a role grant; anything else is a capability grant.
        if (PermitCommandSupport.TryParseManagementRole(token, out ManagementRole role))
        {
            Result<PermitGrantDto> granted = await _permits.GrantRoleAsync(
                context.BroadcasterId,
                target.Value.UserId,
                role,
                invoker.Value,
                expiresAt,
                "!permit",
                ct
            );
            return Result.Success(
                granted.IsSuccess
                    ? $"Granted {role} to {target.Value.Label}."
                    : granted.ErrorMessage ?? "permit: role grant failed"
            );
        }

        Result<PermitGrantDto> grantedCapability = await _permits.GrantCapabilityAsync(
            context.BroadcasterId,
            target.Value.UserId,
            token,
            invoker.Value,
            expiresAt,
            "!permit",
            ct
        );
        return Result.Success(
            grantedCapability.IsSuccess
                ? $"Granted {token} to {target.Value.Label}."
                : grantedCapability.ErrorMessage ?? "permit: capability grant failed"
        );
    }
}

/// <summary>Chat builtin <c>!unpermit @user [role|capability]</c> — revokes one grant, or all when unnamed.</summary>
public sealed class UnpermitBuiltin : IBuiltinCommand
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly ITwitchUsersApi _twitchUsers;

    public UnpermitBuiltin(
        IPermitService permits,
        IUserService users,
        IRoleResolver roles,
        ITwitchUsersApi twitchUsers
    )
    {
        _permits = permits;
        _users = users;
        _roles = roles;
        _twitchUsers = twitchUsers;
    }

    public string BuiltinKey => "unpermit";
    public int DefaultCooldownSeconds => 0;
    public int DefaultMinPermissionLevel => 10; // Moderator on the unified ladder; the real gate is permit:issue below.

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string[] args = context.Args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length < 1)
            return Result.Success("Usage: !unpermit @user [role|capability]");

        Result<Guid> invoker = await PermitBuiltinSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            context,
            "unpermit",
            ct
        );
        if (invoker.IsFailure)
            return Result.Success(invoker.ErrorMessage!);

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            _users,
            _twitchUsers,
            args[0],
            "unpermit",
            ct
        );
        if (target.IsFailure)
            return Result.Success(target.ErrorMessage!);

        // A missing selector revokes ALL of the user's active grants (§3.6).
        string? selector = args.Length >= 2 ? args[1].Trim() : null;

        Result revoked = await _permits.RevokeAsync(
            context.BroadcasterId,
            target.Value.UserId,
            selector,
            invoker.Value,
            ct
        );
        return Result.Success(
            revoked.IsFailure ? revoked.ErrorMessage ?? "unpermit: revoke failed"
            : selector is null ? $"Revoked all permits from {target.Value.Label}."
            : $"Revoked {selector} from {target.Value.Label}."
        );
    }
}
