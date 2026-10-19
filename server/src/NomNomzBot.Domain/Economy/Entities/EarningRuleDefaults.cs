// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// The one place each earning rule default is written. A rule is opt-in: a new rule starts disabled.
/// </summary>
public static class EarningRuleDefaults
{
    public const bool IsEnabled = false;
    public const long Rate = 0;
    public const int ConfigSchemaVersion = 1;
}
