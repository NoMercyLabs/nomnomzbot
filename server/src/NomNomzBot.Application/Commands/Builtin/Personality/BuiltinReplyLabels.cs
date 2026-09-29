// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>
/// The translation keys the reply catalogue serves (commands-pipelines.md §11) — derived from the slot and
/// variable names so the key set can never drift from <see cref="ToneTemplateCatalog"/>. The text itself lives in
/// the dashboard's <c>strings_builtin_replies*.xml</c> (en + nl), guarded by the schema i18n manifest.
/// </summary>
public static class BuiltinReplyLabels
{
    /// <summary>The short name of a reply slot, e.g. "Song added".</summary>
    public static LocalizedText Label(string replyGroup, string slot) =>
        new($"builtin.reply.{replyGroup}.{slot}.label");

    /// <summary>When the bot sends this reply, e.g. "A viewer's song request was added to the queue."</summary>
    public static LocalizedText Description(string replyGroup, string slot) =>
        new($"builtin.reply.{replyGroup}.{slot}.description");

    /// <summary>What a reply variable holds, e.g. "The track's title".</summary>
    public static LocalizedText Variable(string name) => new($"builtin.reply.var.{name}");

    /// <summary>Every key the catalogue can serve — the manifest guard walks this.</summary>
    public static IReadOnlyList<LocalizedText> All() =>
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .SelectMany(s =>
                    new[] { Label(s.BuiltinKey, s.Slot), Description(s.BuiltinKey, s.Slot) }
                ),
            .. ToneTemplateCatalog
                .AllSlots()
                .SelectMany(s => ToneTemplateCatalog.Variables(s.BuiltinKey, s.Slot))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(Variable),
        ];
}
