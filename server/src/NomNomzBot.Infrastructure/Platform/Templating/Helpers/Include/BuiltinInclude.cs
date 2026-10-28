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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Include;

/// <summary>
/// <c>include.builtin:key</c>: the reply of a built-in command, run as the viewer who triggered the template. A
/// template must stay free of side effects, so only a built-in that declares itself read-only for every viewer
/// runs (<see cref="IBuiltinCommand.WhisperSafe"/> and a permission floor of 0, the same gate a whisper uses).
/// Any other key, a failed run or a render without a channel prints the token as written.
/// </summary>
internal sealed class BuiltinInclude(IServiceScopeFactory scopes)
{
    // The nested render is a new template run, so the depth rides on the async flow, not on the render scope.
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>The reply of the named built-in, or null when it cannot be included.</summary>
    public async Task<string?> RenderAsync(
        TemplateRenderScope scope,
        string key,
        CancellationToken cancellationToken
    )
    {
        if (scope.BroadcasterId is not { } broadcasterId || Depth.Value >= CommandInclude.MaxDepth)
            return null;

        using IServiceScope serviceScope = scopes.CreateScope();
        IBuiltinCommand? builtin = serviceScope
            .ServiceProvider.GetRequiredService<IBuiltinCommandCatalog>()
            .Get(key);
        if (builtin is not { WhisperSafe: true, DefaultMinPermissionLevel: 0 })
            return null;

        Depth.Value++;
        try
        {
            Result<string> reply = await builtin.ExecuteAsync(
                ContextFor(scope, broadcasterId, cancellationToken),
                cancellationToken
            );
            return reply.IsSuccess ? reply.Value : null;
        }
        finally
        {
            Depth.Value--;
        }
    }

    private static BuiltinCommandContext ContextFor(
        TemplateRenderScope scope,
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        ChannelContext? channel = scope.Channel;
        return new()
        {
            BroadcasterId = broadcasterId,
            TriggeringUserId = Seed(scope, "user.id"),
            TriggeringUserDisplayName = Seed(scope, "user"),
            TriggeringUserLogin = Seed(scope, "user.name"),
            TriggeringPlatform = scope.Variables.GetValueOrDefault("user.provider"),
            CommandPrefix = channel?.CommandPrefix ?? "!",
            Personality = channel?.Personality ?? PersonalityTone.Informative,
            CancellationToken = cancellationToken,
        };
    }

    private static string Seed(TemplateRenderScope scope, string key) =>
        scope.Variables.GetValueOrDefault(key) ?? string.Empty;
}
