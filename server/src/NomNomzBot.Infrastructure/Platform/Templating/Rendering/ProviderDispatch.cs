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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Rendering;

/// <summary>
/// The provider half of one render. Tokens known before the render are claimed first and grouped by provider
/// and <see cref="TemplateHelperEntry.Source"/>, so each group's <c>PrepareAsync</c> runs once with all its
/// tokens. A token with another token in its key or an argument has no final text before that token rendered,
/// so it prepares on its own, after the inner token rendered, with its rendered arguments.
/// A seed variable with the same key always wins over a provider.
/// </summary>
public sealed class ProviderDispatch(
    ITemplateHelperRegistry helpers,
    TemplateRenderScope scope,
    IReadOnlyDictionary<string, string> seeds
)
{
    private readonly Dictionary<(ITemplateHelperProvider, string?), List<HelperNode>> _claimed = [];
    private readonly HashSet<string> _claimedRaw = new(StringComparer.Ordinal);

    /// <summary>True when the entry that holds this key takes lazy arguments.</summary>
    public bool IsLazy(string key) => helpers.Find(key)?.LazyArguments ?? false;

    /// <summary>True when a provider renders this token (no seed has its key).</summary>
    public bool Owns(RenderedToken token) =>
        !seeds.ContainsKey(token.Content.Trim()) && ProviderOf(token) is not null;

    /// <summary>Queues a token known before the render for its group's one <c>PrepareAsync</c>.</summary>
    public bool Claim(RenderedToken token)
    {
        if (!Owns(token))
            return false;

        HelperNode node = token.ToHelperNode();
        (ITemplateHelperProvider, string?) group = GroupOf(ProviderOf(token)!, node);
        if (!_claimed.TryGetValue(group, out List<HelperNode>? nodes))
            _claimed[group] = nodes = [];
        nodes.Add(node);
        _claimedRaw.Add(node.Raw);
        return true;
    }

    public async Task PrepareClaimedAsync(CancellationToken cancellationToken)
    {
        foreach (((ITemplateHelperProvider provider, _), List<HelperNode> nodes) in _claimed)
        {
            await provider.PrepareAsync(scope, nodes, cancellationToken);
        }
    }

    public async ValueTask<string?> RenderAsync(
        RenderedToken token,
        CancellationToken cancellationToken
    )
    {
        ITemplateHelperProvider provider = ProviderOf(token)!;
        HelperNode node = token.ToHelperNode();
        if (!_claimedRaw.Contains(node.Raw))
            await provider.PrepareAsync(scope, [node], cancellationToken);
        return await provider.RenderAsync(scope, node, cancellationToken);
    }

    private ITemplateHelperProvider? ProviderOf(RenderedToken token) =>
        helpers.ProviderFor(token.Key.Trim());

    private (ITemplateHelperProvider, string?) GroupOf(
        ITemplateHelperProvider provider,
        HelperNode node
    ) => (provider, helpers.Find(node.Key)?.Source);
}
