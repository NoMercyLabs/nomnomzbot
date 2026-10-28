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
