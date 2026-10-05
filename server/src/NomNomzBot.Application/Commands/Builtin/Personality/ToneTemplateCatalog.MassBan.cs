// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>
/// Reply content of the mass-ban consent flow (<c>BuiltinResponseSlots.MassBan.cs</c>). A ban request in somebody
/// else's channel is a formal moderation notice, so every tone shares one plain, professional text; a channel can
/// still override it per slot.
/// </summary>
public static partial class ToneTemplateCatalog
{
    private static void AddMassBanSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.Usage,
            ["prefix"],
            "Usage: {prefix}allow massban"
        );
        AddFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.NothingPending,
            [],
            "There is no mass ban waiting for approval in this channel."
        );
        AddFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.Approved,
            ["massban.count"],
            "Mass ban approved. {massban.count} accounts will be banned now."
        );

        AddFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.Usage,
            ["prefix"],
            "Usage: {prefix}disallow massban"
        );
        AddFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.NothingPending,
            [],
            "There is no mass ban waiting for approval in this channel."
        );
        AddFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.Declined,
            ["massban.count"],
            "Mass ban declined. The {massban.count} accounts will not be banned in this channel."
        );

        AddFormal(
            catalog,
            BuiltinResponseSlots.MassBan.Key,
            BuiltinResponseSlots.MassBan.Request,
            ["massban.channel", "massban.moderator", "massban.count", "prefix"],
            "@{massban.channel} Moderator {massban.moderator} has requested a ban of {massban.count} accounts "
                + "linked to a bot attack. Because the stream is live, no action has been taken yet. The broadcaster, "
                + "a lead moderator or an editor can type {prefix}allow massban to ban them now, or {prefix}disallow "
                + "massban to decline. Without a response, the bans run when the stream ends."
        );
        AddFormal(
            catalog,
            BuiltinResponseSlots.MassBan.Key,
            BuiltinResponseSlots.MassBan.Completed,
            ["massban.banned", "massban.count"],
            "Mass ban complete: {massban.banned} of {massban.count} accounts banned."
        );
    }

    private static void AddMassBanSamples(Dictionary<string, string> samples)
    {
        samples["massban.channel"] = "StreamerName";
        samples["massban.moderator"] = "HelpfulMod";
        samples["massban.count"] = "120";
        samples["massban.banned"] = "118";
    }

    private static void AddFormal(
        Dictionary<(string, string), SlotEntry> catalog,
        string builtinKey,
        string slot,
        string[] variables,
        string text
    ) =>
        Add(
            catalog,
            builtinKey,
            slot,
            variables: variables,
            informative: [text],
            friendly: [text],
            sassy: [text],
            hype: [text],
            chill: [text]
        );
}
