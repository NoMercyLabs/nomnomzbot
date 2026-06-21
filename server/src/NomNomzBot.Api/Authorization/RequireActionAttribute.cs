// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Gate-2 endpoint guard: <c>[RequireAction("economy:config:write")]</c> requires the caller's resolved level
/// to meet the action's effective required level for the current tenant. Sugar over
/// <c>[Authorize(Policy = ActionAuthorizationPolicy.For(actionKey))]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireActionAttribute(string actionKey)
    : AuthorizeAttribute(ActionAuthorizationPolicy.For(actionKey))
{
    public string ActionKey { get; } = actionKey;
}
