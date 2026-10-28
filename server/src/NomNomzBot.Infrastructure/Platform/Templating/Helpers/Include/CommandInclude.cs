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
using NomNomzBot.Application.Common.Picking;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;

/// <summary>
/// Includes the reply text of another command of the channel and renders it for the same viewer. An include
/// cannot run away: the commands already being included travel in the seed variable <see cref="ChainKey"/> of the
/// nested render, and a command that is already in the chain, or a chain of <see cref="MaxDepth"/> commands, stops
/// the include (the token prints as written).
/// </summary>
internal sealed class CommandInclude(IServiceScopeFactory scopes)
{
    /// <summary>The most commands included inside each other.</summary>
    public const int MaxDepth = 5;

    private const string ChainKey = "include.chain";

    /// <summary>The rendered reply of the named command, or null when it cannot be included.</summary>
    public async Task<string?> RenderAsync(
        TemplateRenderScope scope,
        string name,
        CancellationToken cancellationToken
    )
    {
        if (scope.Channel is not { } channel || scope.BroadcasterId is not { } broadcasterId)
            return null;

        CachedCommand? command = Find(channel, name);
        List<string> chain = ChainOf(scope);
        if (
            command is null
            || command.TemplateResponses.Length == 0
            || chain.Count >= MaxDepth
            || chain.Contains(command.Name, StringComparer.OrdinalIgnoreCase)
        )
            return null;

        string reply = NoImmediateRepeatPicker.Pick(
            command.TemplateResponses,
            $"include:{broadcasterId}:{command.Name}"
        );
        using IServiceScope serviceScope = scopes.CreateScope();
        ITemplateResolver resolver =
            serviceScope.ServiceProvider.GetRequiredService<ITemplateResolver>();
        return await resolver.ResolveAsync(
            reply,
            SeedsFor(scope, [.. chain, command.Name]),
            broadcasterId,
            cancellationToken
        );
    }

    /// <summary>A command by name, then by alias; the cache keys its commands by lower-case name.</summary>
    private static CachedCommand? Find(ChannelContext channel, string name) =>
        channel.Commands.TryGetValue(name, out CachedCommand? found)
            ? found
            : channel.Commands.Values.FirstOrDefault(c =>
                c.Aliases.Contains(name, StringComparer.OrdinalIgnoreCase)
            );

    private static List<string> ChainOf(TemplateRenderScope scope) =>
        scope.Variables.TryGetValue(ChainKey, out string? chain) && chain.Length > 0
            ? [.. chain.Split(',')]
            : [];

    /// <summary>The caller's seeds plus the chain, so the nested render sees the same viewer and the include path.</summary>
    private static Dictionary<string, string> SeedsFor(
        TemplateRenderScope scope,
        IEnumerable<string> chain
    )
    {
        Dictionary<string, string> seeds = new(scope.Variables, StringComparer.OrdinalIgnoreCase)
        {
            [ChainKey] = string.Join(",", chain),
        };
        return seeds;
    }
}
