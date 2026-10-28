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

using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>One command a viewer can type: the trigger as typed in chat and the ladder level it needs.</summary>
internal sealed record BotCommand(string Trigger, int Level);

/// <summary>Builds the command lists from the channel cache and the built-in service.</summary>
internal static class BotCommands
{
    /// <summary>
    /// The enabled authored commands of the channel cache, each as typed in chat (its own prefix), sorted by name.
    /// An alias is not a command of its own, and a Regex-matched command has no typeable trigger.
    /// </summary>
    public static IReadOnlyList<BotCommand> Custom(ChannelContext channel) =>
        [
            .. channel
                .Commands.Values.Distinct()
                .Where(c => c.MatchMode != "Regex")
                .OrderBy(c => c.Name.TrimStart('!'), StringComparer.OrdinalIgnoreCase)
                .Select(c => new BotCommand(
                    PrefixOf(c, channel.CommandPrefix) + c.Name.TrimStart('!'),
                    c.MinPermissionLevel
                )),
        ];

    /// <summary>The enabled built-ins in catalogue order, each with the floor the channel applies.</summary>
    public static IReadOnlyList<BotCommand> Builtins(
        IReadOnlyList<BuiltinCommandDto> builtins,
        string channelPrefix
    ) =>
        [
            .. builtins
                .Where(b => b.IsEnabled)
                .Select(b => new BotCommand(
                    channelPrefix + b.BuiltinKey,
                    PermissionLevelNames.ToLevelValue(
                        b.MinPermissionLevelOverride ?? b.DefaultMinPermissionLevel
                    ) ?? 0
                )),
        ];

    /// <summary>Every command, the authored ones first, a trigger once.</summary>
    public static IReadOnlyList<BotCommand> All(
        IReadOnlyList<BotCommand> custom,
        IReadOnlyList<BotCommand> builtins
    ) => [.. custom.Concat(builtins).DistinctBy(c => c.Trigger, StringComparer.OrdinalIgnoreCase)];

    /// <summary>The commands a person of the given role may run (the seed <c>user.role</c>; none means a viewer).</summary>
    public static IReadOnlyList<BotCommand> ForRole(IReadOnlyList<BotCommand> all, string role)
    {
        int level = ChatRole.Parse(role).ToLevelValue();
        return [.. all.Where(c => c.Level <= level)];
    }

    public static string Join(IEnumerable<BotCommand> commands) =>
        string.Join(BotText.ListSeparator, commands.Select(c => c.Trigger));

    // Mirrors the dispatcher's trigger model (ChatMessageHandler.ResolveAuthoredCommand).
    private static string PrefixOf(CachedCommand command, string channelPrefix) =>
        command.PrefixMode switch
        {
            "Custom" => command.CustomPrefix ?? string.Empty,
            "None" => string.Empty,
            _ => channelPrefix,
        };
}
