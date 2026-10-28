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

namespace NomNomzBot.Application.Rewards.Services;

/// <summary>
/// The permittable action keys behind a viewer's own template text. A resolved level that meets the action's
/// default (Subscriber) holds it, and so does a <c>!permit</c> grant to one viewer.
/// </summary>
public static class ViewerTemplateCapabilities
{
    /// <summary>
    /// Have helpers in the text the viewer types resolved: a reward input, or the text after a chat command
    /// (on the <c>use_viewer_templates</c> feature).
    /// </summary>
    public const string RewardInput = "reward:input:template";

    /// <summary>The channel feature that turns viewer templates on. Off until the channel opts in.</summary>
    public const string FeatureKey = "use_viewer_templates";
}
