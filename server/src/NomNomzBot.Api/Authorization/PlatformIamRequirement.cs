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
/// An authorization requirement carrying a Plane-C platform-IAM permission key (e.g. <c>iam:manage</c> —
/// roles-permissions.md §C.1, a DIFFERENT vocabulary from Gate-2 <c>ActionDefinitions</c>). The policy name
/// on <c>[Authorize(Policy = "...")]</c> is the key verbatim (platform-conventions.md §5). Satisfied by
/// <c>PlatformIamAuthorizationHandler</c>.
/// </summary>
public sealed class PlatformIamRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
