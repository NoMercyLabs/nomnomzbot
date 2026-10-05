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
/// The reply "slots" a built-in can send — a slot is one reply CASE (e.g. "live" vs "offline" for uptime), each
/// with its own tone variation-set and declared variables in <see cref="ToneTemplateCatalog"/>. Every sentence a
/// built-in can send is a slot (success, usage, refusal and service-error lines alike), so a channel can re-word
/// any of them (commands-pipelines.md §11). Grouped by reply-group key so the catalog authoring and the built-in
/// code reference one shared set of tokens (no drift). The nested classes live in one partial file per domain.
/// Tone only touches copy the SYSTEM authors — a streamer's own override is never tone-styled.
/// </summary>
public static partial class BuiltinResponseSlots
{
    /// <summary>
    /// Built-ins whose replies are authored under ANOTHER built-in's reply group — the trigger word differs but
    /// the replies are the same (<c>!unlurk</c> speaks with the <c>lurk</c> lines, <c>!profile</c> with the
    /// <c>stats</c> line). Every other built-in's reply group is its own key.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SharedReplyGroups = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["unlurk"] = Lurk.Key,
        ["profile"] = Stats.Key,
    };

    /// <summary>
    /// The slots the pre-§11 single <c>responseTemplate</c> override fed, per reply group. A legacy override
    /// stored on a <c>ChannelBuiltinCommand</c> row still applies to exactly these slots (unless the slot has its
    /// own entry) until the next write rewrites it into the per-slot shape — so an existing override survives.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> LegacyOverrideSlots =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Uptime.Key] = [Uptime.Live],
            [Song.Key] = [Song.Playing],
            [Queue.Key] = [Queue.List],
            [SongRequest.Key] = [SongRequest.Added],
            [Commands.Key] = [Commands.List],
            [Lurk.Key] = [Lurk.Lurking, Lurk.NotLurking],
            [AccountAge.Key] = [AccountAge.Age],
            [FollowAge.Key] = [FollowAge.Age],
            [Discord.Key] = [Discord.Invite],
            [Leaderboard.Key] = [Leaderboard.Top],
            [Playlist.Key] = [Playlist.Summary],
            [Forgetme.Key] = [Forgetme.Done],
            [Stats.Key] = [Stats.Profile],
        };

    /// <summary>
    /// The reply group a built-in speaks with — its own key unless it shares another built-in's replies. Leading
    /// "!" and case are normalized away, matching how the chat handler parses a trigger.
    /// </summary>
    public static string ReplyGroupFor(string builtinKey)
    {
        string normalized = builtinKey.TrimStart('!').ToLowerInvariant();
        return SharedReplyGroups.GetValueOrDefault(normalized, normalized);
    }

    /// <summary>The slots a legacy <c>responseTemplate</c> on a row for <paramref name="builtinKey"/> applies to.</summary>
    public static IReadOnlyList<string> LegacySlotsFor(string builtinKey) =>
        LegacyOverrideSlots.GetValueOrDefault(ReplyGroupFor(builtinKey)) ?? [];
}
