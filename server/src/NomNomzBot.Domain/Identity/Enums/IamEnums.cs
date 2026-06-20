// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}

/// <summary>Whether a platform IAM principal is a human employee or a service account.</summary>
public enum IamPrincipalType
{
    Employee,
    ServiceAccount,
}

/// <summary>The outcome recorded on an IAM access evaluation (audit).</summary>
public enum IamOutcome
{
    Allowed,
    Denied,
}
