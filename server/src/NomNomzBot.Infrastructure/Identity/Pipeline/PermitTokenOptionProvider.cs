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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Identity.Pipeline;

/// <summary>
/// Supplies what <c>permit</c> and <c>unpermit</c> can grant, for the <c>permit_token</c> picker kind: the management
/// roles (the <see cref="ManagementRole"/> values, saved as their lower-case names) first, then every capability the
/// channel's action matrix marks as grantable through a permit (saved as its action key, with the matrix description as
/// the secondary text). A saved role alias such as <c>mod</c> finds the role it stands for. An action matrix that
/// cannot be read is unavailable with the service's reason.
/// </summary>
internal sealed class PermitTokenOptionProvider : IPipelineOptionProvider
{
    private readonly IActionAuthorizationService _actions;

    public PermitTokenOptionProvider(IActionAuthorizationService actions)
    {
        _actions = actions;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.PermitToken;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ActionPermissionDto>> matrix = await _actions.GetActionMatrixAsync(
            broadcasterId,
            ct
        );
        if (matrix.IsFailure)
            return PipelineOptionPage.Unavailable(matrix.ErrorMessage!);

        List<PipelineOption> roles =
        [
            .. Enum.GetValues<ManagementRole>()
                .Select(r => PipelineOptionPage.Option(RoleValue(r), r.ToString(), "Role")),
        ];
        List<PipelineOption> capabilities =
        [
            .. matrix
                .Value.Where(a => a.IsGrantableViaPermit)
                .Select(a => PipelineOptionPage.Option(a.ActionKey, a.ActionKey, a.Description)),
        ];

        return PipelineOptionPage.Of(
            [.. roles, .. capabilities],
            WithRoleAlias(request),
            inSourceOrder: true
        );
    }

    private static string RoleValue(ManagementRole role) => role.ToString().ToLowerInvariant();

    /// <summary>A saved alias (<c>mod</c>, <c>supermod</c>) becomes the role value the list carries.</summary>
    private static PipelineOptionQuery WithRoleAlias(PipelineOptionQuery request) =>
        request.IsValueLookup
        && PermitCommandSupport.TryParseManagementRole(request.Value!, out ManagementRole role)
            ? request with
            {
                Value = RoleValue(role),
            }
            : request;
}
