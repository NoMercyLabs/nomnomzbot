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
/// Pipeline action <c>permit</c> — the <c>!permit</c> flow as a pipeline step (roles-permissions §3.6/§7). Grants
/// the resolved target user a management ROLE (when the token names one) or a single CAPABILITY (action key). The
/// invoker is gated on <c>permit:issue</c> in-action, and <see cref="IPermitService"/> re-asserts the no-escalation
/// + <c>IsGrantableViaPermit</c> default-deny guardrails, so the action is safe to run from any pipeline.
/// Parameters (all optional; chat <c>!permit @user &lt;token&gt;</c> supplies them via variables):
/// <c>target_variable</c> (default <c>target.id</c>), <c>role_or_capability</c> (default <c>args.1</c>),
/// <c>duration_minutes</c> (0 = permanent).
/// </summary>
public sealed class PermitAction : ICommandAction
{
    private readonly IPermitService _permits;
    private readonly IUserService _users;
    private readonly IRoleResolver _roles;
    private readonly TimeProvider _clock;

    public string ActionType => "permit";

    public LocalizedText Category => new("pipeline.category.identity");

    public LocalizedText Description => new("pipeline.permit.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "target_variable",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.permit.target_variable.help")
            ),
            new(
                "role_or_capability",
                PipelineActionFieldKind.Text,
                Required: true,
                Description: new("pipeline.permit.role_or_capability.help")
            ),
            new(
                "duration_minutes",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.permit.duration_minutes.help")
            ),
        ];

    public PermitAction(
        IPermitService permits,
        IUserService users,
        IRoleResolver roles,
        TimeProvider clock
    )
    {
        _permits = permits;
        _users = users;
        _roles = roles;
        _clock = clock;
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
            "permit"
        );
        if (invoker.IsFailure)
            return ActionResult.Failure(invoker.ErrorMessage!);

        Result<Guid> target = await PermitCommandSupport.ResolveTargetAsync(
            _users,
            ctx,
            action,
            "permit"
        );
        if (target.IsFailure)
            return ActionResult.Failure(target.ErrorMessage!);

        string? token =
            action.GetString("role_or_capability") ?? ctx.Variables.GetValueOrDefault("args.1");
        if (string.IsNullOrWhiteSpace(token))
            return ActionResult.Failure(
                "permit: name a role or capability — usage: !permit @user <role|capability>"
            );
        token = token.Trim();

        int durationMinutes = action.GetInt("duration_minutes", 0);
        DateTime? expiresAt =
            durationMinutes > 0 ? _clock.GetUtcNow().UtcDateTime.AddMinutes(durationMinutes) : null;

        string targetLabel = PermitCommandSupport.TargetLabel(ctx);

        // A token that names a management role is a role grant; anything else is a capability (action-key) grant.
        if (PermitCommandSupport.TryParseManagementRole(token, out ManagementRole role))
        {
            Result<PermitGrantDto> granted = await _permits.GrantRoleAsync(
                ctx.BroadcasterId,
                target.Value,
                role,
                invoker.Value,
                expiresAt,
                "!permit",
                ctx.CancellationToken
            );
            return granted.IsSuccess
                ? ActionResult.Success($"Granted {role} to {targetLabel}.")
                : ActionResult.Failure(granted.ErrorMessage ?? "permit: role grant failed");
        }

        Result<PermitGrantDto> grantedCapability = await _permits.GrantCapabilityAsync(
            ctx.BroadcasterId,
            target.Value,
            token,
            invoker.Value,
            expiresAt,
            "!permit",
            ctx.CancellationToken
        );
        return grantedCapability.IsSuccess
            ? ActionResult.Success($"Granted {token} to {targetLabel}.")
            : ActionResult.Failure(
                grantedCapability.ErrorMessage ?? "permit: capability grant failed"
            );
    }
}
