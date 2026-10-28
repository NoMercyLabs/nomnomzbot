// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Resolves a viewer's own text in the <see cref="TemplateHelperContext.Viewer"/> surface: only the helpers that
/// row lists, only the viewer's own variables, and hard limits. A text that breaks a limit comes back as written.
/// </summary>
public interface IViewerTemplateResolver
{
    /// <param name="text">The viewer's text, as typed.</param>
    /// <param name="viewerVariables">The viewer's own seed variables (see <c>TemplateSubject.SeedUser</c>) and nothing else.</param>
    /// <param name="broadcasterId">The channel the text belongs to.</param>
    /// <param name="limits">What the render may do.</param>
    Task<ViewerTemplateResult> ResolveViewerAsync(
        string text,
        IDictionary<string, string> viewerVariables,
        Guid broadcasterId,
        ViewerTemplateLimits limits,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The limits of one viewer render.</summary>
/// <param name="MaxDepth">The deepest nesting of tokens inside tokens (a lone token is depth 1).</param>
/// <param name="MaxTextCount">The most copies or the widest pad one <c>text.*</c> helper may make.</param>
/// <param name="MaxOutputLength">The longest rendered text, in UTF-16 characters.</param>
public sealed record ViewerTemplateLimits(int MaxDepth, int MaxTextCount, int MaxOutputLength)
{
    /// <summary>Nesting depth 3 and at most 20 copies, with the caller's output cap.</summary>
    public static ViewerTemplateLimits WithOutputCap(int maxOutputLength) =>
        new(MaxDepth: 3, MaxTextCount: 20, MaxOutputLength: maxOutputLength);
}

/// <summary>Why a viewer render gave the text back as written.</summary>
public enum ViewerTemplateRefusal
{
    None,
    TooDeep,
    TooLong,
}

/// <summary>The outcome of one viewer render: <see cref="Text"/> is the rendered text, or the original when refused.</summary>
public sealed record ViewerTemplateResult(string Text, ViewerTemplateRefusal Refusal);
