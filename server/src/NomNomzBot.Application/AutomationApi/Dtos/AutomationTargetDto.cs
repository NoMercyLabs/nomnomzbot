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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.AutomationApi.Dtos;

/// <summary>
/// The wire shape of one <see cref="BindingTarget"/> on the automation API: <c>kind</c> is
/// <c>command | pipeline | action</c> (any casing); <c>id</c> names the command or pipeline; an action names its
/// <c>actionType</c> and carries its <c>params</c> instead.
/// </summary>
public sealed record AutomationTargetDto(
    string Kind,
    string? Id = null,
    string? ActionType = null,
    IReadOnlyDictionary<string, JsonElement>? Params = null
)
{
    public static AutomationTargetDto From(BindingTarget target) =>
        new(target.Kind.ToString().ToLowerInvariant(), target.Id, target.ActionType, target.Params);

    /// <summary>
    /// Reads the wire shape into a target. Fails with <c>VALIDATION_FAILED</c> for an unknown kind, a pipeline id
    /// that is not a guid, a command or pipeline without an id, or an action without an action type.
    /// </summary>
    public Result<BindingTarget> ToTarget()
    {
        if (
            !Enum.TryParse(Kind, ignoreCase: true, out BindingTargetKind kind)
            || !Enum.IsDefined(kind)
        )
            return Invalid("The target kind must be command, pipeline or action.");

        switch (kind)
        {
            case BindingTargetKind.Action when string.IsNullOrWhiteSpace(ActionType):
                return Invalid("An action target needs an actionType.");
            case BindingTargetKind.Pipeline when !Guid.TryParse(Id, out Guid _):
                return Invalid("A pipeline target needs a pipeline id (a guid).");
            case BindingTargetKind.Command when string.IsNullOrWhiteSpace(Id):
                return Invalid("A command target needs a command id.");
            default:
                return Result.Success(new BindingTarget(kind, Id, ActionType, Params));
        }
    }

    private static Result<BindingTarget> Invalid(string message) =>
        Result.Failure<BindingTarget>(message, "VALIDATION_FAILED");
}
