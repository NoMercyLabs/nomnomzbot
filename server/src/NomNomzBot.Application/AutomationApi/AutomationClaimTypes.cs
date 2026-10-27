// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.AutomationApi;

/// <summary>The claim names on the principal the automation token scheme builds.</summary>
public static class AutomationClaimTypes
{
    public const string TokenId = "automation:token_id";
    public const string TokenName = "automation:token_name";
    public const string BroadcasterId = "automation:broadcaster_id";
}
