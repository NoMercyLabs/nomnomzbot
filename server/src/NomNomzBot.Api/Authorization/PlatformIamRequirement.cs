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
/// An authorization requirement carrying a Plane-C platform-IAM permission key (e.g. <c>iam:manage</c> —
/// roles-permissions.md §C.1, a DIFFERENT vocabulary from Gate-2 <c>ActionDefinitions</c>). The policy name
/// on <c>[Authorize(Policy = "...")]</c> is the key verbatim (platform-conventions.md §5). Satisfied by
/// <c>PlatformIamAuthorizationHandler</c>.
/// </summary>
public sealed class PlatformIamRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
