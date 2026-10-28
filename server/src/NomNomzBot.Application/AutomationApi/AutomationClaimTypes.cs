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

namespace NomNomzBot.Application.AutomationApi;

/// <summary>The claim names on the principal the automation token scheme builds.</summary>
public static class AutomationClaimTypes
{
    public const string TokenId = "automation:token_id";
    public const string TokenName = "automation:token_name";
    public const string BroadcasterId = "automation:broadcaster_id";
}
