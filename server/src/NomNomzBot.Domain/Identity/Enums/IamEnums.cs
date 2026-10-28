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

namespace NomNomzBot.Domain.Identity.Enums;

// Plane C — platform IAM enums (roles-permissions §1.1). [VC:enum] text-stored; the member name is the token.

/// <summary>The category an <c>IamPermission</c> belongs to (least-privilege bundling).</summary>
public enum IamCategory
{
    Tenant,
    Billing,
    Audit,
    Iam,
    FeatureFlag,

    /// <summary>Platform content authoring/propagation (platform-admin.md §4).</summary>
    Content,
}

/// <summary>Whether a platform IAM principal is a human employee or a service account.</summary>
public enum IamPrincipalType
{
    Employee,
    ServiceAccount,
}

/// <summary>The outcome recorded on an IAM access evaluation (audit). <c>Allowed</c>/<c>Denied</c> cover a
/// plain access evaluation; <c>Failed</c>/<c>Partial</c> cover a multi-step action's own result — e.g. a
/// <c>content:publish</c> fan-out that failed mid-way (platform-admin.md §5).</summary>
public enum IamOutcome
{
    Allowed,
    Denied,
    Failed,
    Partial,
}
