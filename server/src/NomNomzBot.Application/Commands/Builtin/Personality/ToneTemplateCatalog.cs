// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>
/// The code-defined personality content: for each <c>(tone, builtinKey, slot)</c> a set of 2–4 VARIED
/// templates written in that tone's voice, using the real template variables the built-in seeds. A tone is a
/// named variation-set; <see cref="Pick"/> chooses one at random (the same "pick a random variation" idea the
/// custom-command <c>PickResponse</c>/<c>PickRandomAsync</c> paths use).
///
/// <para>
/// Authoring is grouped by <c>(builtinKey, slot)</c>, each declaring all five tones. When a specific tone has
/// no entry for a slot, resolution falls back to <see cref="PersonalityTone.Informative"/> so a channel always
/// gets a sensible line; when the whole slot is absent the built-in's own neutral fallback is used instead.
/// </para>
/// </summary>
public static class ToneTemplateCatalog
{
    /// <summary>
    /// The variation-sets for <paramref name="tone"/> at <c>(<paramref name="builtinKey"/>,
    /// <paramref name="slot"/>)</c>. Falls back to <see cref="PersonalityTone.Informative"/> when the tone
    /// itself has no entry; empty when the slot is not in the catalog at all.
    /// </summary>
    public static IReadOnlyList<string> Get(string? tone, string builtinKey, string slot)
    {
        if (
            !Catalog.TryGetValue(
                (builtinKey, slot),
                out IReadOnlyDictionary<string, string[]>? byTone
            )
        )
            return [];

        string normalized = PersonalityTone.Normalize(tone);
        if (byTone.TryGetValue(normalized, out string[]? variations) && variations.Length > 0)
            return variations;

        return byTone.TryGetValue(PersonalityTone.Informative, out string[]? informative)
            ? informative
            : [];
    }

    /// <summary>
    /// One random template for <c>(tone, builtinKey, slot)</c>, or <c>null</c> when the slot has no templates
    /// (so the caller can fall back to its own neutral string).
    /// </summary>
    public static string? Pick(string? tone, string builtinKey, string slot)
    {
        IReadOnlyList<string> variations = Get(tone, builtinKey, slot);
        if (variations.Count == 0)
            return null;
        return variations.Count == 1
            ? variations[0]
            : variations[Random.Shared.Next(variations.Count)];
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Content. Grouped by (builtinKey, slot); every slot declares all five tones.
    //  Templates use the variables the built-in seeds (see BuiltinResponseSlots docs).
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<
        (string BuiltinKey, string Slot),
        IReadOnlyDictionary<string, string[]>
    > Catalog = Build();

    private static IReadOnlyDictionary<
        (string, string),
        IReadOnlyDictionary<string, string[]>
    > Build()
    {
        Dictionary<(string, string), IReadOnlyDictionary<string, string[]>> catalog = new();

        // ── !uptime / live ({uptime} = real elapsed time) ──────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Live,
            informative:
            [
                "Live for {uptime}.",
                "The stream has been live for {uptime}.",
                "Uptime: {uptime}.",
            ],
            friendly:
            [
                "We've been hanging out for {uptime} now — thanks for being here! 💛",
                "Live and loving it for {uptime}!",
                "{uptime} of stream so far — so glad you're here!",
            ],
            sassy:
            [
                "We've been live {uptime}. Yes, some of us have places to be. 😏",
                "{uptime} and still going strong — try to keep up.",
                "Clock says {uptime}. No, you can't have those hours back.",
            ],
            hype:
            [
                "LIVE FOR {uptime} AND WE'RE JUST GETTING STARTED 🔥🔥",
                "{uptime} ON THE CLOCK LETS GOOOO 🚀",
                "{uptime} OF PURE CHAOS AND WE AIN'T STOPPING 💪",
            ],
            chill:
            [
                "live for {uptime}, no rush.",
                "{uptime} in. just vibing.",
                "been {uptime}. all good.",
            ]
        );

        // ── !uptime / offline ──────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Offline,
            informative:
            [
                "The stream is currently offline.",
                "We're offline right now.",
                "Not live at the moment.",
            ],
            friendly:
            [
                "We're offline right now — catch you next stream! 💛",
                "No stream going yet, but I'm glad you stopped by!",
                "Offline for now — see you soon!",
            ],
            sassy:
            [
                "Offline. Shocking, I know. 😏",
                "No stream right now. Try again when the sun's up.",
                "We're offline — go touch some grass.",
            ],
            hype:
            [
                "WE'RE OFFLINE... FOR NOW 👀 FOLLOW SO YOU DON'T MISS THE NEXT ONE 🔔",
                "NO STREAM YET BUT STAY READY 🚀",
                "OFFLINE BUT NEVER FORGOTTEN — SEE YOU SOON 🔥",
            ],
            chill: ["offline rn.", "not live atm.", "we're off. later."]
        );

        // ── !song / playing ({song.status} {song.name} {song.artist}) ──────────
        Add(
            catalog,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Playing,
            informative:
            [
                "{song.status} {song.name} by {song.artist}",
                "Now playing: {song.name} by {song.artist}.",
                "Currently playing {song.name} by {song.artist}.",
            ],
            friendly:
            [
                "🎶 We're vibing to {song.name} by {song.artist} — great pick!",
                "Now playing {song.name} by {song.artist}. Enjoy! 💛",
                "This one's {song.name} by {song.artist} 🎧",
            ],
            sassy:
            [
                "It's {song.name} by {song.artist}. You're welcome. 😏",
                "{song.name} by {song.artist} — yes, again.",
                "Currently {song.name} by {song.artist}. Bold choice by someone.",
            ],
            hype:
            [
                "🔊 {song.name} BY {song.artist} — TUNE! 🔥",
                "WE ARE BLASTING {song.name} BY {song.artist} 🎶🚀",
                "{song.name} BY {song.artist} AND IT GOES HARD 💥",
            ],
            chill:
            [
                "{song.status} {song.name} — {song.artist}.",
                "playing {song.name} by {song.artist}.",
                "{song.name}, {song.artist}. nice.",
            ]
        );

        // ── !song / nothing ────────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Nothing,
            informative:
            [
                "Nothing is playing right now.",
                "No track is currently playing.",
                "The player is idle at the moment.",
            ],
            friendly:
            [
                "Nothing playing at the moment — request something with !sr! 💛",
                "Quiet right now! Drop an !sr to get the music going.",
                "No song yet — your pick could be next!",
            ],
            sassy:
            [
                "Nothing's playing. The silence is deafening, isn't it? 😏",
                "No music. Someone do something about that.",
                "Dead air. Request a song with !sr, hero.",
            ],
            hype:
            [
                "NO SONG PLAYING?! FIX THAT WITH !sr RIGHT NOW 🔥🎶",
                "SILENCE?! WE DON'T DO THAT HERE — !sr NOW 🚀",
                "PLAYER'S EMPTY, DROP AN !sr AND LETS GOOO 💥",
            ],
            chill: ["nothing playing rn.", "quiet atm. !sr if you want.", "no song. it's fine."]
        );

        // ── !queue / list ({queue.count} {queue.list} {queue.next} {queue.more}) ─
        Add(
            catalog,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.List,
            informative:
            [
                "Queue ({queue.count}): {queue.list}",
                "Up next: {queue.list}",
                "{queue.count} in the queue: {queue.list}",
            ],
            friendly:
            [
                "Here's what's coming up 🎶 {queue.list}",
                "Queue's looking good ({queue.count})! {queue.list}",
                "Next up for us: {queue.list} 💛",
            ],
            sassy:
            [
                "{queue.count} songs deep: {queue.list}. Patience. 😏",
                "The queue, since you asked: {queue.list}",
                "Up next, whether you like it or not: {queue.list}",
            ],
            hype:
            [
                "🔥 {queue.count} BANGERS LOADED: {queue.list}",
                "THE QUEUE IS STACKED 🚀 {queue.list}",
                "COMING UP AND IT'S HEAT: {queue.list} 💥",
            ],
            chill:
            [
                "queue: {queue.list}",
                "up next: {queue.list}",
                "{queue.count} lined up: {queue.list}",
            ]
        );

        // ── !queue / empty ─────────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.Empty,
            informative:
            [
                "The queue is empty.",
                "Nothing in the queue right now.",
                "No songs queued.",
            ],
            friendly:
            [
                "Queue's empty — add one with !sr! 💛",
                "Nothing lined up yet. Your !sr could be first!",
                "Empty queue! Get something going with !sr.",
            ],
            sassy:
            [
                "Queue's empty. Someone's slacking. 😏",
                "Nothing queued. Tragic. Use !sr.",
                "Empty. As in, do an !sr already.",
            ],
            hype:
            [
                "QUEUE'S EMPTY?! LOAD IT UP WITH !sr 🔥🎶",
                "NOTHING QUEUED — CHANGE THAT NOW WITH !sr 🚀",
                "EMPTY QUEUE ALERT 🚨 !sr TO THE RESCUE",
            ],
            chill: ["queue's empty.", "nothing queued. !sr maybe.", "empty rn."]
        );

        // ── !sr / added ({user} {track.name} {track.artist}) ───────────────────
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Added,
            informative:
            [
                "@{user} Added {track.name} by {track.artist} to the queue.",
                "@{user} Queued: {track.name} by {track.artist}.",
                "@{user} {track.name} by {track.artist} is in the queue.",
            ],
            friendly:
            [
                "@{user} Added {track.name} by {track.artist} — great choice! 💛",
                "@{user} Got it! {track.name} by {track.artist} is queued 🎶",
                "@{user} {track.name} by {track.artist} coming up — thanks!",
            ],
            sassy:
            [
                "@{user} Fine, {track.name} by {track.artist} is queued. 😏",
                "@{user} Added {track.name} by {track.artist}. Bold. Noted.",
                "@{user} {track.name} by {track.artist}? Sure. It's in.",
            ],
            hype:
            [
                "@{user} 🔥 {track.name} BY {track.artist} LOCKED IN THE QUEUE 🚀",
                "@{user} ADDED {track.name} BY {track.artist} — LETS GOOO 🎶💥",
                "@{user} {track.name} BY {track.artist} INCOMING 🔊",
            ],
            chill:
            [
                "@{user} added {track.name} by {track.artist}.",
                "@{user} queued {track.name}. nice.",
                "@{user} {track.name} by {track.artist}, in.",
            ]
        );

        // ── !sr / notfound ({user} {query}) ────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotFound,
            informative:
            [
                "@{user} No tracks found for \"{query}\".",
                "@{user} I couldn't find \"{query}\".",
                "@{user} Nothing matched \"{query}\".",
            ],
            friendly:
            [
                "@{user} Hmm, couldn't find \"{query}\" — try another spelling? 💛",
                "@{user} No luck with \"{query}\". Give it another go!",
                "@{user} Couldn't find \"{query}\", but don't give up!",
            ],
            sassy:
            [
                "@{user} \"{query}\"? Never heard of it. 😏",
                "@{user} Found nothing for \"{query}\". Try real words.",
                "@{user} \"{query}\" doesn't exist. Allegedly.",
            ],
            hype:
            [
                "@{user} NOTHING FOUND FOR \"{query}\" 😤 TRY AGAIN, WE BELIEVE IN YOU 🔥",
                "@{user} \"{query}\"?! NO HITS — RELOAD AND RETRY 🚀",
                "@{user} SWING AND A MISS ON \"{query}\" — GO AGAIN 💪",
            ],
            chill:
            [
                "@{user} nothing for \"{query}\".",
                "@{user} couldn't find \"{query}\". oh well.",
                "@{user} no match for \"{query}\".",
            ]
        );

        // ── !skip / skipped ────────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Skipped,
            informative: ["Skipped.", "Track skipped.", "Skipped the current track."],
            friendly:
            [
                "Skipped! On to the next one 🎶",
                "Done — skipped it for you! 💛",
                "Next up! Skipped that one.",
            ],
            sassy:
            [
                "Skipped. That one was rough anyway. 😏",
                "Gone. You're welcome.",
                "Skipped. Someone had to.",
            ],
            hype:
            [
                "SKIPPED ⏭️ NEXT BANGER INCOMING 🔥",
                "OUTTA HERE — NEXT ONE LETS GO 🚀",
                "SKIPPED 💥 ON TO THE HEAT",
            ],
            chill: ["skipped.", "next one. skipped.", "gone. moving on."]
        );

        // ── !stats / profile ({stats.user} {stats.messages} {stats.watchtime}
        //    {stats.points} {stats.firstseen}) — Informative is intentionally OMITTED so the default tone
        //    keeps the built-in's richer, conditional stats line (rank + streak). The four flavored tones
        //    deviate from it. ──────────────────────────────────────────────────
        AddFlavored(
            catalog,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.Profile,
            friendly:
            [
                "{stats.user}, you've sent {stats.messages} messages and earned {stats.points} points — {stats.watchtime} watched together! 💛",
                "Look at {stats.user}: {stats.points} points, {stats.messages} messages, here since {stats.firstseen} 🎉",
                "{stats.user} has been amazing — {stats.watchtime} watched and {stats.points} points!",
            ],
            sassy:
            [
                "{stats.user}: {stats.messages} messages, {stats.points} points. Touch grass? Never. 😏",
                "{stats.user} has {stats.watchtime} of watch time. We're not judging. (We are.)",
                "{stats.points} points and {stats.messages} messages, {stats.user}. Impressive. Concerning. Both.",
            ],
            hype:
            [
                "{stats.user} 🔥 {stats.points} POINTS · {stats.messages} MESSAGES · {stats.watchtime} WATCHED — LEGEND 🚀",
                "BIG NUMBERS FOR {stats.user} 💥 {stats.points} POINTS AND {stats.watchtime} WATCHED",
                "{stats.user} IS BUILT DIFFERENT: {stats.messages} MESSAGES, {stats.points} POINTS 🔥",
            ],
            chill:
            [
                "{stats.user}: {stats.messages} msgs, {stats.watchtime}, {stats.points} pts.",
                "{stats.user} — {stats.points} points, around since {stats.firstseen}.",
                "{stats.user}: {stats.watchtime} watched, {stats.points} pts. solid.",
            ]
        );

        return catalog;
    }

    /// <summary>Registers one slot's five tone variation-sets. Every tone is required, keeping the catalog complete.</summary>
    private static void Add(
        Dictionary<(string, string), IReadOnlyDictionary<string, string[]>> catalog,
        string builtinKey,
        string slot,
        string[] informative,
        string[] friendly,
        string[] sassy,
        string[] hype,
        string[] chill
    )
    {
        catalog[(builtinKey, slot)] = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [PersonalityTone.Informative] = informative,
            [PersonalityTone.Friendly] = friendly,
            [PersonalityTone.Sassy] = sassy,
            [PersonalityTone.Hype] = hype,
            [PersonalityTone.Chill] = chill,
        };
    }

    /// <summary>
    /// Registers a slot's four FLAVORED tones with no Informative entry — so the default (Informative) tone
    /// resolves to the built-in's own neutral fallback instead of a catalog template. Used where the built-in's
    /// neutral line is already the ideal precise/default phrasing (e.g. the rich <c>!stats</c> line).
    /// </summary>
    private static void AddFlavored(
        Dictionary<(string, string), IReadOnlyDictionary<string, string[]>> catalog,
        string builtinKey,
        string slot,
        string[] friendly,
        string[] sassy,
        string[] hype,
        string[] chill
    )
    {
        catalog[(builtinKey, slot)] = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [PersonalityTone.Friendly] = friendly,
            [PersonalityTone.Sassy] = sassy,
            [PersonalityTone.Hype] = hype,
            [PersonalityTone.Chill] = chill,
        };
    }
}
