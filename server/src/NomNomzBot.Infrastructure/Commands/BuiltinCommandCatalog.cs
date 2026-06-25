// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Singleton catalog of all built-in platform commands.
/// Mirrors the entries in <c>DefaultCommandsSeeder</c> — keep both in sync.
/// </summary>
public sealed class BuiltinCommandCatalog : IBuiltinCommandCatalog
{
    private static readonly IReadOnlyList<BuiltinCommandDefinition> Entries =
    [
        new(
            "!sr",
            "everyone",
            5,
            "Request a song",
            """{"steps":[{"action":{"type":"music_request"}}]}"""
        ),
        new(
            "!skip",
            "moderator",
            0,
            "Skip the current song",
            """{"steps":[{"action":{"type":"music_skip"}}]}"""
        ),
        new(
            "!queue",
            "everyone",
            10,
            "Show the song queue",
            """{"steps":[{"action":{"type":"music_queue"}}]}"""
        ),
        new(
            "!volume",
            "moderator",
            0,
            "Set the music volume",
            """{"steps":[{"action":{"type":"music_volume"}}]}"""
        ),
        new(
            "!song",
            "everyone",
            5,
            "Show the current song",
            """{"steps":[{"action":{"type":"music_current"}}]}"""
        ),
    ];

    private static readonly HashSet<string> Names = new(
        Entries.Select(e => e.Name),
        StringComparer.OrdinalIgnoreCase
    );

    public IReadOnlyList<BuiltinCommandDefinition> GetAll() => Entries;

    public bool IsBuiltin(string name) => Names.Contains(name);
}
