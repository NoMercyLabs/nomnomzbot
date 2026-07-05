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
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity.PipelineActions;

/// <summary>
/// Pipeline action <c>unpermit</c> — the <c>!unpermit</c> flow (roles-permissions §3.6). Revokes a target user's
/// active permit grant(s): the named role/capability token, or ALL of their active grants when no token is given.
/// Gated on <c>permit:issue</c> in-action; <see cref="IPermitService"/> soft-deletes and emits the revoke event(s).
/// Parameters (optional): <c>target_variable</c> (default <c>target.id</c>), <c>role_or_capability</c>
/// (default <c>args.1</c>; omit to revoke everything).
/// </summary>
public sealed class UnpermitAction : ICommandAction
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;

    public string ActionType => "unpermit";

    public UnpermitAction(IPermitService permits, IUserService users, IRoleResolver roles)
    {
        _permits = permits;
        _users = users;
        _roles = roles;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        Result<Guid> invoker = await PermitCommandSupport.AuthorizeInvokerAsync(
            _users,
            _roles,
            ctx,
            "unpermit"
        );
        if (invoker.IsFailure)
            return ActionResult.Failure(invoker.ErrorMessage!);

        Result<Guid> target = await PermitCommandSupport.ResolveTargetAsync(
            _users,
            ctx,
            action,
            "unpermit"
        );
        if (target.IsFailure)
            return ActionResult.Failure(target.ErrorMessage!);

        // A null/blank selector revokes ALL of the user's active grants (§3.6).
        string? selector =
            action.GetString("role_or_capability") ?? ctx.Variables.GetValueOrDefault("args.1");
        selector = string.IsNullOrWhiteSpace(selector) ? null : selector.Trim();

        Result revoked = await _permits.RevokeAsync(
            ctx.BroadcasterId,
            target.Value,
            selector,
            invoker.Value,
            ctx.CancellationToken
        );
        if (revoked.IsFailure)
            return ActionResult.Failure(revoked.ErrorMessage ?? "unpermit: revoke failed");

        string targetLabel = PermitCommandSupport.TargetLabel(ctx);
        return ActionResult.Success(
            selector is null
                ? $"Revoked all permits from {targetLabel}."
                : $"Revoked {selector} from {targetLabel}."
        );
    }
}
