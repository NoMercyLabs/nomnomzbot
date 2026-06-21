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
/// An authorization requirement carrying a roles-permissions action key (e.g. <c>economy:config:write</c>).
/// Satisfied by <c>ActionAuthorizationHandler</c> via Gate 2 (roles-permissions §3.3 / §6).
/// </summary>
public sealed class ActionAuthorizationRequirement(string actionKey) : IAuthorizationRequirement
{
    public string ActionKey { get; } = actionKey;
}
