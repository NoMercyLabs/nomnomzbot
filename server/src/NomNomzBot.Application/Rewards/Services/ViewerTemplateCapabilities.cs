// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Rewards.Services;

/// <summary>
/// The permittable action keys behind a viewer's own template text. A resolved level that meets the action's
/// default (Subscriber) holds it, and so does a <c>!permit</c> grant to one viewer.
/// </summary>
public static class ViewerTemplateCapabilities
{
    /// <summary>Have helpers in the viewer's reward input resolved (on the <c>use_viewer_templates</c> feature).</summary>
    public const string RewardInput = "reward:input:template";

    /// <summary>The channel feature that turns viewer templates on. Off until the channel opts in.</summary>
    public const string FeatureKey = "use_viewer_templates";
}
