// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>The viewer surface of the one template engine: a viewer's own text, with hard limits.</summary>
public sealed partial class TemplateResolver : IViewerTemplateResolver
{
    public async Task<ViewerTemplateResult> ResolveViewerAsync(
        string text,
        IDictionary<string, string> viewerVariables,
        Guid broadcasterId,
        ViewerTemplateLimits limits,
        CancellationToken cancellationToken = default
    )
    {
        if (NestingDepth(TemplateParser.Parse(text)) > limits.MaxDepth)
            return new ViewerTemplateResult(text, ViewerTemplateRefusal.TooDeep);

        string rendered = await ResolveCoreAsync(
            text,
            viewerVariables,
            broadcasterId,
            limits,
            cancellationToken
        );
        return rendered.Length > limits.MaxOutputLength
            ? new ViewerTemplateResult(text, ViewerTemplateRefusal.TooLong)
            : new ViewerTemplateResult(rendered, ViewerTemplateRefusal.None);
    }

    /// <summary>A viewer's token may render when it is one of the viewer's own seed variables or a helper of the Viewer context.</summary>
    private static bool IsViewerKey(
        string key,
        IReadOnlyDictionary<string, string> seeds,
        IReadOnlyList<TemplateHelperEntry> viewerHelpers
    ) => seeds.ContainsKey(key) || viewerHelpers.Any(entry => entry.Matches(key));

    /// <summary>The depth of the deepest token: a lone token is 1, a token inside its argument is 2.</summary>
    private static int NestingDepth(IReadOnlyList<TemplateNode> nodes)
    {
        int deepest = 0;
        foreach (PlaceholderNode node in nodes.OfType<PlaceholderNode>())
        {
            int inner = NestingDepth(node.Key);
            foreach (IReadOnlyList<TemplateNode> argument in node.Arguments ?? [])
                inner = Math.Max(inner, NestingDepth(argument));
            deepest = Math.Max(deepest, 1 + inner);
        }
        return deepest;
    }
}
