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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chatter;

/// <summary>
/// The random chatter rows (catalog section 7.5): <c>random.chatter</c>, <c>random.chatter.notMe</c> and
/// <c>random.chatter.mention</c>, drawn from the viewers who chatted since the stream began
/// (<c>ChannelContext.SessionChatters</c>). The <c>random</c> namespace already has a provider, so these rows
/// live in a provider of their own; the registry keys rows, not namespaces. A render without a channel prints the
/// token as written; a channel nobody chatted in yet renders empty. The public constructor draws from
/// <see cref="Random.Shared"/>; a test hands the other one a seeded <see cref="Random"/>.
/// </summary>
public sealed class ChatterTemplateHelpers(Random random) : ITemplateHelperProvider
{
    public ChatterTemplateHelpers()
        : this(Random.Shared) { }

    public string Namespace => "chatter";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        Row("random.chatter", "ViewerOne"),
        Row("random.chatter.notMe", "ViewerTwo"),
        Row("random.chatter.mention", "@ViewerOne"),
    ];

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (scope.Channel is null || node.Arguments.Count != 0)
            return ValueTask.FromResult<string?>(null);

        string key = node.Key.ToLowerInvariant();
        List<string> names = key == "random.chatter.notme" ? OthersOf(scope) : AllOf(scope);
        if (names.Count == 0)
            return ValueTask.FromResult<string?>(string.Empty);

        string drawn = names[random.Next(names.Count)];
        return ValueTask.FromResult<string?>(key == "random.chatter.mention" ? "@" + drawn : drawn);
    }

    /// <summary>Every chatter's display name in a fixed order, so a seeded draw is repeatable.</summary>
    private static List<string> AllOf(TemplateRenderScope scope) =>
        [
            .. scope
                .Channel!.SessionChatters.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal),
        ];

    /// <summary>
    /// The chatters without the viewer who triggered the render: by platform id (<c>user.id</c>) when the render
    /// knows it, else by display name (<c>user</c>).
    /// </summary>
    private static List<string> OthersOf(TemplateRenderScope scope)
    {
        scope.Variables.TryGetValue("user.id", out string? userId);
        scope.Variables.TryGetValue("user", out string? display);

        return
        [
            .. scope
                .Channel!.SessionChatters.Where(pair =>
                    userId is not null
                        ? !string.Equals(pair.Key, userId, StringComparison.OrdinalIgnoreCase)
                        : !string.Equals(pair.Value, display, StringComparison.OrdinalIgnoreCase)
                )
                .Select(pair => pair.Value)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal),
        ];
    }

    private static TemplateHelperEntry Row(string key, string sample) =>
        ComposingRows.Row(key, sample, TemplateHelperFreshness.Memory);
}
