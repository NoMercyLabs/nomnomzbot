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
/// Decides whether a viewer's reward input may hold template helpers, and resolves it when it may. The text stays
/// exactly as typed unless the channel enabled <c>use_viewer_templates</c> and the viewer holds the
/// <c>reward:input:template</c> action.
/// </summary>
public interface IViewerInputTemplateService
{
    /// <summary>
    /// The text to use for the reward: the resolved text, or <paramref name="input"/> as typed when the feature is off,
    /// the viewer lacks the action, the text breaks a limit, or it holds no template syntax.
    /// </summary>
    /// <param name="isTts">True when the reward speaks the text; the output cap is then the channel's TTS cap.</param>
    /// <param name="audit">True for a live redemption; a replay never writes an audit event.</param>
    Task<string> ResolveRewardInputAsync(
        Guid broadcasterId,
        ViewerInputIdentity viewer,
        string input,
        bool isTts,
        bool audit,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The viewer who typed the reward input, as the redemption event names them.</summary>
public sealed record ViewerInputIdentity(string TwitchUserId, string? Login, string DisplayName);
