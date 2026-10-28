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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating.Dates;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Legacy;
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;
using NomNomzBot.Infrastructure.Platform.Templating.Rendering;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// The one template engine (catalog section 6). It parses the template once, then renders it inside out:
/// a seed variable always wins; a key whose namespace has an <see cref="ITemplateHelperProvider"/> goes to
/// that provider (its <c>PrepareAsync</c> runs once per source for all its tokens, then <c>RenderAsync</c>);
/// every other key goes through the as-built <see cref="LegacyTemplateHelpers"/>. An unknown key renders as written.
/// </summary>
public sealed partial class TemplateResolver : ITemplateResolver
{
    private readonly LegacyTemplateHelpers _legacy;
    private readonly ITemplateHelperRegistry _helpers;
    private readonly IChannelRegistry _channels;
    private readonly TimeProvider _timeProvider;

    /// <summary>A resolver with the as-built helpers only (no provider).</summary>
    public TemplateResolver(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<TemplateResolver> logger,
        TimeProvider timeProvider
    )
        : this(
            scopeFactory,
            registry,
            logger,
            timeProvider,
            TemplateHelperRegistryAggregate.LegacyOnly
        ) { }

    public TemplateResolver(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<TemplateResolver> logger,
        TimeProvider timeProvider,
        ITemplateHelperRegistry helpers
    )
    {
        _legacy = new LegacyTemplateHelpers(scopeFactory, registry, logger, timeProvider);
        _helpers = helpers;
        _channels = registry;
        _timeProvider = timeProvider;
    }

    /// <summary>Simple synchronous resolve using only provided variables (no channel: dates render in UTC).</summary>
    public string Resolve(string template, IDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        DateExpressionContext dateContext = _legacy.DateContextFor(null);
        return TemplateTreeRenderer.Render(
            TemplateParser.Parse(template),
            token =>
                FindVariable(variables, token.Content.Trim())
                ?? _legacy.RenderExpression(token.Content, dateContext)
        );
    }

    /// <summary>Full async resolve with lazy lookups: only the keys the template uses are fetched.</summary>
    public Task<string> ResolveAsync(
        string template,
        IDictionary<string, string> seedVariables,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => ResolveCoreAsync(template, seedVariables, broadcasterId, null, cancellationToken);

    /// <summary>
    /// The one render. With <paramref name="viewer"/> set it is a viewer's own text: no pre-pass runs (they read
    /// custom data), a token whose key is neither a seed nor a helper of the Viewer context prints as written, and
    /// the text helpers cut their counts to the limit.
    /// </summary>
    private async Task<string> ResolveCoreAsync(
        string template,
        IDictionary<string, string> seedVariables,
        Guid? broadcasterId,
        ViewerTemplateLimits? viewer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (viewer is null)
            template = await _legacy.ExpandPrePassesAsync(
                template,
                broadcasterId,
                cancellationToken
            );
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse(template);
        if (!nodes.OfType<PlaceholderNode>().Any())
            return TemplateTreeRenderer.Render(nodes, _ => null);

        Dictionary<string, string> seeds = new(seedVariables, StringComparer.OrdinalIgnoreCase);
        TemplateRenderScope scope = new()
        {
            BroadcasterId = broadcasterId,
            Channel = broadcasterId is null ? null : _channels.Get(broadcasterId.Value),
            Variables = seeds,
            Now = _timeProvider.GetUtcNow(),
            TextCountLimit = viewer?.MaxTextCount,
        };
        ProviderDispatch providers = new(_helpers, scope, seeds);
        IReadOnlyList<TemplateHelperEntry> viewerHelpers = viewer is null
            ? []
            : _helpers.ForContext(TemplateHelperContext.Viewer);
        bool Permitted(RenderedToken token) =>
            viewer is null || IsViewerKey(token.Key.Trim(), seeds, viewerHelpers);

        HashSet<string> needed = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlaceholderNode node in TemplateTree.StaticPlaceholders(nodes, providers.IsLazy))
        {
            RenderedToken token = RenderedToken.FromStatic(node, providers.IsLazy);
            if (!Permitted(token))
                continue;

            if (!providers.Claim(token))
                needed.Add(token.Content.Trim());
        }

        Dictionary<string, string> vars = await _legacy.BuildVariablesAsync(
            seedVariables,
            needed,
            broadcasterId,
            cancellationToken
        );
        await providers.PrepareClaimedAsync(cancellationToken);

        DateExpressionContext dateContext = _legacy.DateContextFor(broadcasterId);
        scope.ResolveKeyAsync = (key, _) =>
            EvaluateAsync(new RenderedToken(key, null, "{" + key + "}"));
        return await TemplateTreeRenderer.RenderAsync(nodes, EvaluateAsync, providers.IsLazy);

        async ValueTask<string?> EvaluateAsync(RenderedToken token)
        {
            if (!Permitted(token))
                return null;

            if (providers.Owns(token))
                return await providers.RenderAsync(token, cancellationToken);
            string key = token.Content.Trim();
            await FetchLateAsync(key);
            // A name written by {set:name=value} reads last: it shadows no registered key and no seed.
            return LegacyTemplateHelpers.Lookup(key, vars)
                ?? _legacy.RenderExpression(token.Content, dateContext)
                ?? scope.SetVariables.GetValueOrDefault(key);
        }

        // A token inside a lazy argument was not known before the render: fetch its variable when it is asked for.
        async Task FetchLateAsync(string key)
        {
            if (vars.ContainsKey(key) || !needed.Add(key))
                return;

            Dictionary<string, string> late = await _legacy.BuildVariablesAsync(
                seedVariables,
                [key],
                broadcasterId,
                cancellationToken
            );
            foreach (KeyValuePair<string, string> pair in late)
                vars.TryAdd(pair.Key, pair.Value);
        }
    }

    private static string? FindVariable(IDictionary<string, string> variables, string key)
    {
        foreach (KeyValuePair<string, string> kvp in variables)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value ?? string.Empty;
        }
        return null;
    }
}
