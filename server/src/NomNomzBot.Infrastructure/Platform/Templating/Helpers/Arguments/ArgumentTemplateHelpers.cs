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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// The one provider of the <c>args</c> namespace (catalog section 7.16). It owns no helper itself: it finds every
/// <see cref="IArgumentHelperGroup"/> by reflection, merges their rows and asks each group in turn to render a
/// node. Every helper reads the command seed (<c>args</c>) and is pure, so there is nothing to prepare.
/// The seed keys <c>args</c> and <c>args.&lt;n&gt;</c> stay with the legacy helpers: a seed always wins.
/// </summary>
public sealed class ArgumentTemplateHelpers : ITemplateHelperProvider
{
    private readonly IReadOnlyList<IArgumentHelperGroup> _groups;
    private readonly ArgumentUserHelpers? _users;

    /// <summary>The pure rows only: a host with no services (a test of the word rows) uses this one.</summary>
    public ArgumentTemplateHelpers()
        : this(DiscoverGroups(), null) { }

    /// <summary>The dependency-injected provider: the pure rows plus <c>args.&lt;n&gt;.user.*</c>, which reads viewer data.</summary>
    public ArgumentTemplateHelpers(IServiceScopeFactory scopes, IServiceProvider services)
        : this(scopes, services.GetService<IViewerDirectory>() ?? new DbViewerDirectory()) { }

    internal ArgumentTemplateHelpers(IServiceScopeFactory scopes, IViewerDirectory directory)
        : this(DiscoverGroups(), new ArgumentUserHelpers(scopes, directory)) { }

    private ArgumentTemplateHelpers(
        IEnumerable<IArgumentHelperGroup> groups,
        ArgumentUserHelpers? users
    )
    {
        _groups = [.. groups];
        _users = users;
        Entries = [.. _groups.SelectMany(g => g.Entries).Concat(users?.Entries ?? [])];
    }

    public string Namespace => "args";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) =>
        _users is null
            ? ValueTask.CompletedTask
            : _users.PrepareAsync(scope, nodes, cancellationToken);

    public ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (!CommandWords.TryRead(scope, out CommandWords? words))
            return ValueTask.FromResult<string?>(null);

        if (_users is not null && ArgumentUserHelpers.Claims(node.Key))
            return _users.RenderAsync(scope, node, cancellationToken);

        foreach (IArgumentHelperGroup group in _groups)
        {
            string? rendered = group.TryRender(node, words);
            if (rendered is not null)
                return ValueTask.FromResult<string?>(rendered);
        }
        return ValueTask.FromResult<string?>(null);
    }

    private static IEnumerable<IArgumentHelperGroup> DiscoverGroups() =>
        typeof(ArgumentTemplateHelpers)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(IArgumentHelperGroup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (IArgumentHelperGroup)Activator.CreateInstance(t)!);
}
