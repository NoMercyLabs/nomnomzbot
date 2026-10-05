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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
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
///
/// Every reply is a slot of the <c>permit</c> / <c>unpermit</c> reply group. The two groups share the slot
/// names for the caller and target checks, so the failure codes below map to one slot name for either.
/// </summary>
internal static class PermitBuiltinSupport
{
    private const string PermitIssueActionKey = "permit:issue";

    private const string AccountUnresolvedCode = "ACCOUNT_UNRESOLVED";
    private const string NoTargetCode = "NO_TARGET";
    internal const string TargetNotFoundCode = "TARGET_NOT_FOUND";
    private const string TargetUnresolvedCode = "TARGET_UNRESOLVED";
    private const string ForbiddenCode = "FORBIDDEN";

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
            return Result.Failure<Guid>(
                $"{verb}: your account could not be resolved",
                AccountUnresolvedCode
            );

        Result<bool> mayIssue = await roles.HasCapabilityAsync(
            invokerId,
            context.BroadcasterId,
            PermitIssueActionKey,
            ct
        );
        return mayIssue is { IsSuccess: true, Value: true }
            ? Result.Success(invokerId)
            : Result.Failure<Guid>(
                $"{verb}: you are not allowed to manage permits (needs {PermitIssueActionKey})",
                ForbiddenCode
            );
    }

    /// <summary>
    /// Resolves an @mention to the target's platform user Guid (Helix login → id → User). A failure carries the
    /// parsed login in <c>ErrorDetail</c> so the reply can name it.
    /// </summary>
    public static async Task<Result<(Guid UserId, string Label)>> ResolveTargetAsync(
        IUserService users,
        ITwitchUsersApi twitchUsers,
        string mention,
        string verb,
        CancellationToken ct
    )
    {
        string login = MentionParser.ParseUserMention(mention).ToLowerInvariant();
        if (login.Length == 0)
            return Result.Failure<(Guid, string)>(
                $"{verb}: no target — mention a user with @name",
                NoTargetCode
            );

        Result<IReadOnlyList<TwitchUser>> lookup = await twitchUsers.GetUsersByLoginsAsync(
            [login],
            ct
        );
        TwitchUser? twitchUser = lookup.IsSuccess ? lookup.Value.FirstOrDefault() : null;
        if (twitchUser is null)
            return Result.Failure<(Guid, string)>(
                $"{verb}: '{login}' was not found on Twitch",
                TargetNotFoundCode,
                login
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
                TargetUnresolvedCode,
                login
            );

        return Result.Success((userId, twitchUser.DisplayName));
    }

    /// <summary>
    /// The chat reply for a failed <see cref="AuthorizeInvokerAsync"/> or <see cref="ResolveTargetAsync"/>: the
    /// failure code picks the slot, the service message stays for logs.
    /// </summary>
    public static async Task<Result<string>> ReplyToFailureAsync(
        IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        string verb,
        Result failure,
        CancellationToken ct
    )
    {
        Dictionary<string, string> user = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = failure.ErrorDetail ?? string.Empty,
        };

        return failure.ErrorCode switch
        {
            AccountUnresolvedCode => await ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Permit.AccountUnresolved,
                $"{verb}: your account could not be resolved",
                null,
                ct
            ),
            ForbiddenCode => await ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Permit.NotAllowed,
                $"{verb}: you are not allowed to manage permits (needs {PermitIssueActionKey})",
                null,
                ct
            ),
            TargetNotFoundCode => await ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Permit.TargetNotFound,
                $"{verb}: '{{user}}' was not found on Twitch",
                user,
                ct
            ),
            TargetUnresolvedCode => await ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Permit.TargetUnresolved,
                $"{verb}: the target user could not be resolved",
                null,
                ct
            ),
            _ => await ReplyAsync(
                composer,
                context,
                verb,
                BuiltinResponseSlots.Permit.NoTarget,
                $"{verb}: no target — mention a user with @name",
                null,
                ct
            ),
        };
    }

    /// <summary>Composes one reply of the <paramref name="verb"/> group (<c>permit</c> or <c>unpermit</c>).</summary>
    public static async Task<Result<string>> ReplyAsync(
        IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        string verb,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await composer.ComposeAsync(context, verb, slot, neutralFallback, variables, ct)
        );

    public static Dictionary<string, string> Vars(params (string Name, string Value)[] values) =>
        new(
            values.Select(v => KeyValuePair.Create(v.Name, v.Value)),
            StringComparer.OrdinalIgnoreCase
        );

    /// <summary>True when a grant failed for the service's authorization refusal (FORBIDDEN).</summary>
    public static bool IsRefusal(Result failure) => failure.ErrorCode == ForbiddenCode;
}

/// <summary>Chat builtin <c>!permit @user &lt;role|capability&gt; [minutes]</c> — grants a bounded delegation.</summary>
public sealed class PermitBuiltin : IBuiltinCommand
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly TimeProvider _clock;
    private readonly IBuiltinResponseComposer _composer;

    public PermitBuiltin(
        IPermitService permits,
        IUserService users,
        IRoleResolver roles,
        ITwitchUsersApi twitchUsers,
        TimeProvider clock,
        IBuiltinResponseComposer composer
    )
    {
        _permits = permits;
        _users = users;
        _roles = roles;
        _twitchUsers = twitchUsers;
        _clock = clock;
        _composer = composer;
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
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Permit.Usage,
                "Usage: !permit @user <role|capability> [minutes]",
                null,
                ct
            );

        Result<Guid> invoker = await PermitBuiltinSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            context,
            BuiltinKey,
            ct
        );
        if (invoker.IsFailure)
            return await PermitBuiltinSupport.ReplyToFailureAsync(
                _composer,
                context,
                BuiltinKey,
                invoker,
                ct
            );

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            _users,
            _twitchUsers,
            args[0],
            BuiltinKey,
            ct
        );
        if (target.IsFailure)
            return await PermitBuiltinSupport.ReplyToFailureAsync(
                _composer,
                context,
                BuiltinKey,
                target,
                ct
            );

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
            if (granted.IsFailure)
                return PermitBuiltinSupport.IsRefusal(granted)
                    ? await ReplyAsync(
                        context,
                        BuiltinResponseSlots.Permit.RoleTooHigh,
                        "Cannot permit a role above your own level.",
                        null,
                        ct
                    )
                    : await GrantFailedAsync(context, ct);

            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Permit.GrantedRole,
                "Granted {permit.role} to {user}.",
                PermitBuiltinSupport.Vars(
                    ("permit.role", role.ToString()),
                    ("user", target.Value.Label)
                ),
                ct
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
        Dictionary<string, string> capabilityVars = PermitBuiltinSupport.Vars(
            ("permit.capability", token),
            ("user", target.Value.Label)
        );
        if (grantedCapability.IsFailure)
            return PermitBuiltinSupport.IsRefusal(grantedCapability)
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Permit.CapabilityDenied,
                    "You can't permit '{permit.capability}' — it must exist, be delegable, and be one you hold yourself.",
                    capabilityVars,
                    ct
                )
                : await GrantFailedAsync(context, ct);

        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Permit.GrantedCapability,
            "Granted {permit.capability} to {user}.",
            capabilityVars,
            ct
        );
    }

    private Task<Result<string>> GrantFailedAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    ) =>
        ReplyAsync(
            context,
            BuiltinResponseSlots.Permit.GrantFailed,
            "The permit could not be granted.",
            null,
            ct
        );

    private Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        PermitBuiltinSupport.ReplyAsync(
            _composer,
            context,
            BuiltinKey,
            slot,
            neutralFallback,
            variables,
            ct
        );
}

/// <summary>Chat builtin <c>!unpermit @user [role|capability]</c> — revokes one grant, or all when unnamed.</summary>
public sealed class UnpermitBuiltin : IBuiltinCommand
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly ITwitchUsersApi _twitchUsers;
    private readonly IBuiltinResponseComposer _composer;

    public UnpermitBuiltin(
        IPermitService permits,
        IUserService users,
        IRoleResolver roles,
        ITwitchUsersApi twitchUsers,
        IBuiltinResponseComposer composer
    )
    {
        _permits = permits;
        _users = users;
        _roles = roles;
        _twitchUsers = twitchUsers;
        _composer = composer;
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
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Unpermit.Usage,
                "Usage: !unpermit @user [role|capability]",
                null,
                ct
            );

        Result<Guid> invoker = await PermitBuiltinSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            context,
            BuiltinKey,
            ct
        );
        if (invoker.IsFailure)
            return await PermitBuiltinSupport.ReplyToFailureAsync(
                _composer,
                context,
                BuiltinKey,
                invoker,
                ct
            );

        Result<(Guid UserId, string Label)> target = await PermitBuiltinSupport.ResolveTargetAsync(
            _users,
            _twitchUsers,
            args[0],
            BuiltinKey,
            ct
        );
        if (target.IsFailure)
            return await PermitBuiltinSupport.ReplyToFailureAsync(
                _composer,
                context,
                BuiltinKey,
                target,
                ct
            );

        // A missing selector revokes ALL of the user's active grants (§3.6).
        string? selector = args.Length >= 2 ? args[1].Trim() : null;

        Result revoked = await _permits.RevokeAsync(
            context.BroadcasterId,
            target.Value.UserId,
            selector,
            invoker.Value,
            ct
        );
        if (revoked.IsFailure)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Unpermit.RevokeFailed,
                "The permit could not be revoked.",
                null,
                ct
            );

        return selector is null
            ? await ReplyAsync(
                context,
                BuiltinResponseSlots.Unpermit.RevokedAll,
                "Revoked all permits from {user}.",
                PermitBuiltinSupport.Vars(("user", target.Value.Label)),
                ct
            )
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Unpermit.Revoked,
                "Revoked {permit.capability} from {user}.",
                PermitBuiltinSupport.Vars(
                    ("permit.capability", selector),
                    ("user", target.Value.Label)
                ),
                ct
            );
    }

    private Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        PermitBuiltinSupport.ReplyAsync(
            _composer,
            context,
            BuiltinKey,
            slot,
            neutralFallback,
            variables,
            ct
        );
}
