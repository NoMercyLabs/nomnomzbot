// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Catalog of built-in platform commands that ship pre-seeded on every channel.
/// These are the commands the <c>DefaultCommandsSeeder</c> inserts; the catalog is the
/// queryable definition source so the UI and validators can enumerate them without
/// hitting the database.
/// </summary>
public interface IBuiltinCommandCatalog
{
    /// <summary>Returns every built-in command definition, in seed order.</summary>
    IReadOnlyList<BuiltinCommandDefinition> GetAll();

    /// <summary>Returns <c>true</c> when <paramref name="name"/> matches a built-in command name (case-insensitive).</summary>
    bool IsBuiltin(string name);
}

/// <summary>Describes a built-in command as shipped.</summary>
public sealed record BuiltinCommandDefinition(
    /// <summary>Command trigger, e.g. <c>!sr</c>. Always prefixed with <c>!</c>.</summary>
    string Name,
    /// <summary>Permission level required to use the command.</summary>
    string Permission,
    /// <summary>Global cooldown in seconds.</summary>
    int CooldownSeconds,
    /// <summary>Human-readable description surfaced in the dashboard.</summary>
    string Description,
    /// <summary>Inline pipeline JSON that backs this command.</summary>
    string PipelineJson
);
