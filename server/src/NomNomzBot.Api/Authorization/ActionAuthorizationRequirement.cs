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
/// An authorization requirement carrying a roles-permissions action key (e.g. <c>economy:config:write</c>).
/// Satisfied by <c>ActionAuthorizationHandler</c> via Gate 2 (roles-permissions §3.3 / §6).
/// </summary>
public sealed class ActionAuthorizationRequirement(string actionKey) : IAuthorizationRequirement
{
    public string ActionKey { get; } = actionKey;
}
