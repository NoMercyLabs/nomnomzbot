// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Picking;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>
/// The code-defined reply content: for each <c>(builtinKey, slot)</c> the variables the built-in seeds and, per
/// tone, a set of 1–4 VARIED templates written in that tone's voice. A tone is a named variation-set;
/// <see cref="Pick"/> chooses one at random (the same "pick a random variation" idea the custom-command
/// <c>PickResponse</c>/<c>PickRandomAsync</c> paths use). Every slot here is a reply a channel can re-word
/// (commands-pipelines.md §11) — the catalogue the dashboard's reply editor lists.
///
/// <para>
/// Authoring is grouped by <c>(builtinKey, slot)</c>, each declaring all five tones; the entries live in one
/// partial file per domain. When a specific tone has no entry for a slot, resolution falls back to
/// <see cref="PersonalityTone.Informative"/> so a channel always gets a sensible line.
/// </para>
/// </summary>
public static partial class ToneTemplateCatalog
{
    /// <summary>
    /// The variation-sets for <paramref name="tone"/> at <c>(<paramref name="builtinKey"/>,
    /// <paramref name="slot"/>)</c>. Falls back to <see cref="PersonalityTone.Informative"/> when the tone
    /// itself has no entry; empty when the slot is not in the catalog at all.
    /// </summary>
    public static IReadOnlyList<string> Get(string? tone, string builtinKey, string slot)
    {
        if (!Catalog.TryGetValue((builtinKey, slot), out SlotEntry? entry))
            return [];

        string normalized = PersonalityTone.Normalize(tone);
        if (entry.Tones.TryGetValue(normalized, out string[]? variations) && variations.Length > 0)
            return variations;

        return entry.Tones.TryGetValue(PersonalityTone.Informative, out string[]? informative)
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
        return NoImmediateRepeatPicker.Pick(
            variations,
            $"{builtinKey}:{slot}:{PersonalityTone.Normalize(tone)}"
        );
    }

    /// <summary>Every <c>(builtinKey, slot)</c> the catalog authors, ordered by key then slot.</summary>
    public static IReadOnlyList<(string BuiltinKey, string Slot)> AllSlots() =>
        [
            .. Catalog
                .Keys.OrderBy(k => k.BuiltinKey, StringComparer.Ordinal)
                .ThenBy(k => k.Slot, StringComparer.Ordinal),
        ];

    /// <summary>True when the catalog authors <c>(builtinKey, slot)</c>.</summary>
    public static bool Contains(string builtinKey, string slot) =>
        Catalog.ContainsKey((builtinKey, slot));

    /// <summary>The variables the built-in seeds for <c>(builtinKey, slot)</c> — empty for an unknown slot.</summary>
    public static IReadOnlyList<string> Variables(string builtinKey, string slot) =>
        Catalog.TryGetValue((builtinKey, slot), out SlotEntry? entry) ? entry.Variables : [];

    /// <summary>
    /// The shipped wording a channel on the default (Informative) tone sees for the slot — its first
    /// Informative variation — or null when the slot ships only flavoured tones (the built-in's own fallback).
    /// </summary>
    public static string? ShippedTemplate(string builtinKey, string slot) =>
        Catalog.TryGetValue((builtinKey, slot), out SlotEntry? entry)
        && entry.Tones.TryGetValue(PersonalityTone.Informative, out string[]? informative)
        && informative.Length > 0
            ? informative[0]
            : null;

    /// <summary>
    /// A fixed example value for a declared variable — what the dashboard's live preview fills in. Data, not UI
    /// copy: a name, a number, a track. Empty for a variable with no sample.
    /// </summary>
    public static string SampleValue(string variable) =>
        Samples.GetValueOrDefault(variable, string.Empty);

    private static readonly IReadOnlyDictionary<string, string> Samples = BuildSamples();

    private static IReadOnlyDictionary<string, string> BuildSamples()
    {
        Dictionary<string, string> samples = new(StringComparer.OrdinalIgnoreCase);
        AddMusicSamples(samples);
        AddCoreSamples(samples);
        AddCommunitySamples(samples);
        return samples;
    }

    /// <summary>One slot's declared variables and its per-tone variation-sets.</summary>
    private sealed record SlotEntry(
        IReadOnlyList<string> Variables,
        IReadOnlyDictionary<string, string[]> Tones
    );

    // ─────────────────────────────────────────────────────────────────────────
    //  Content. Grouped by (builtinKey, slot); every slot declares its variables and all five tones.
    //  Templates use only the variables the slot declares (plus registered template helpers).
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<
        (string BuiltinKey, string Slot),
        SlotEntry
    > Catalog = Build();

    private static IReadOnlyDictionary<(string, string), SlotEntry> Build()
    {
        Dictionary<(string, string), SlotEntry> catalog = new();
        AddMusicSlots(catalog);
        AddCoreSlots(catalog);
        AddCommunitySlots(catalog);

        // ── !uptime / live ({uptime} = real elapsed time) ──────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Live,
            variables: ["uptime"],
            informative:
            [
                "Live for {uptime}.",
                "The stream has been live for {uptime}.",
                "Uptime: {uptime}.",
            ],
            friendly:
            [
                "We've been hanging out for {uptime} now — thanks for being here!",
                "Live and loving it for {uptime}!",
                "{uptime} of stream so far — so glad you're here!",
            ],
            sassy:
            [
                "We've been live for {uptime}. Yes, the whole time. I counted. It's my whole job.",
                "The clock says {uptime}. The clock does not lie. Unlike \"one more game\" from two hours ago.",
                "{uptime}. That's how long we've been live. What you did with that time is between you and your browser history.",
                "Live for {uptime} and still no plan. Consistency is important.",
            ],
            hype:
            [
                "LIVE FOR {uptime} AND STILL CLIMBING. NOBODY IS TIRED. NOT EVEN THE BOT.",
                "{uptime} ON THE CLOCK. THE GRIND DOES NOT SLEEP.",
                "{uptime} DEEP AND WE ARE JUST WARMING UP. BUCKLE UP.",
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
            variables: [],
            informative:
            [
                "The stream is currently offline.",
                "We're offline right now.",
                "Not live at the moment.",
            ],
            friendly:
            [
                "We're offline right now — catch you next stream!",
                "No stream going yet, but I'm glad you stopped by!",
                "Offline for now — see you soon!",
            ],
            sassy:
            [
                "Offline. You just typed !uptime into an empty room. Bold.",
                "The stream is off. It's just me in here. It's very peaceful. Don't ruin it.",
                "No stream right now. Somewhere out there, the streamer is pretending to have a life.",
                "Offline. Uptime: zero. Some questions answer themselves.",
            ],
            hype:
            [
                "WE ARE OFFLINE... FOR NOW. STAY READY.",
                "NO STREAM YET. THE CALM BEFORE THE STORM.",
                "OFFLINE, NOT DEFEATED. SEE YOU AT THE NEXT ONE.",
            ],
            chill: ["offline rn.", "not live atm.", "we're off. later."]
        );

        // ── !song / playing ({song.status} {song.name} {song.artist}) ──────────
        Add(
            catalog,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Playing,
            variables:
            [
                "song.artist",
                "song.attribution",
                "song.link",
                "song.name",
                "song.provider",
                "song.requester",
                "song.source",
                "song.status",
            ],
            informative: ["The current song is: {song.name} by {song.artist} {song.link}"],
            friendly:
            [
                "We're vibing to {song.name} by {song.artist} — great pick! {song.link}",
                "Now playing {song.name} by {song.artist}. Enjoy! {song.link}",
                "This one's {song.name} by {song.artist}. {song.link}",
            ],
            sassy:
            [
                "It's {song.name} by {song.artist}. You could have read the overlay, but I'm flattered you asked. {song.link}",
                "{song.name} by {song.artist}. Yes, again. No, I don't pick them. I just endure them. {song.link}",
                "Currently {song.name} by {song.artist}. Bold choice by someone. Not naming names. {song.link}",
                "{song.name} by {song.artist}. The court will note nobody skipped it. Yet. {song.link}",
            ],
            hype:
            [
                "{song.name} BY {song.artist}. ABSOLUTE TUNE. TURN IT UP. {song.link}",
                "WE ARE BLASTING {song.name} BY {song.artist}. NEIGHBORS BEWARE. {song.link}",
                "{song.name} BY {song.artist} AND IT GOES HARD. THAT'S THE TWEET. {song.link}",
            ],
            chill:
            [
                "{song.status} {song.name} — {song.artist}. {song.link}",
                "playing {song.name} by {song.artist}. {song.link}",
                "{song.name}, {song.artist}. nice. {song.link}",
            ]
        );

        // ── !song / nothing ────────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Nothing,
            variables: [],
            informative: ["No song is currently playing!"],
            friendly:
            [
                "Nothing playing at the moment — request something with !sr!",
                "Quiet right now! Drop an !sr to get the music going.",
                "No song yet — your pick could be next!",
            ],
            sassy:
            [
                "Nothing is playing. Just the sound of nobody using !sr. Fix that or don't. I'm a bot, not a cop.",
                "No music. The queue died of neglect. !sr if you feel responsible. You should.",
                "Dead air. I'd put something on myself, but apparently \"bots choosing the music\" is \"how we got here last time\".",
                "Silence. Somewhere, a DJ weeps. !sr, hero.",
            ],
            hype:
            [
                "NO SONG PLAYING. FIX IT WITH !sr RIGHT NOW.",
                "SILENCE? WE DON'T DO SILENCE HERE. !sr, GO.",
                "THE PLAYER IS EMPTY. DROP AN !sr AND SAVE US ALL.",
            ],
            chill: ["nothing playing rn.", "quiet atm. !sr if you want.", "no song. it's fine."]
        );

        // ── !queue / list ({queue.count} {queue.list} {queue.next} {queue.more}) ─
        Add(
            catalog,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.List,
            variables: ["queue.count", "queue.list", "queue.more", "queue.next"],
            informative:
            [
                "Queue ({queue.count}): {queue.list}",
                "Up next: {queue.list}",
                "{queue.count} in the queue: {queue.list}",
            ],
            friendly:
            [
                "Here's what's coming up: {queue.list}",
                "Queue's looking good ({queue.count})! {queue.list}",
                "Next up for us: {queue.list}",
            ],
            sassy:
            [
                "{queue.count} songs deep: {queue.list}. Want your own spot? !queue @you.",
                "OFFICIAL QUEUE REPORT: {queue.count} tracks. {queue.list}. Complaints go to /dev/null.",
                "Up next, whether you like it or not: {queue.list}",
                "The queue, since you asked instead of scrolling: {queue.list}",
            ],
            hype:
            [
                "{queue.count} BANGERS LOADED: {queue.list}",
                "THE QUEUE IS STACKED: {queue.list}",
                "COMING UP AND IT'S ALL HEAT: {queue.list}",
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
            variables: [],
            informative:
            [
                "The queue is empty.",
                "Nothing in the queue right now.",
                "No songs queued.",
            ],
            friendly:
            [
                "Queue's empty — add one with !sr!",
                "Nothing lined up yet. Your !sr could be first!",
                "Empty queue! Get something going with !sr.",
            ],
            sassy:
            [
                "The queue is empty. It's not going to fill itself. That's what !sr is for. That's the whole deal.",
                "Nothing queued. The DJ booth is a ghost town. Somebody !sr before I start playing elevator music.",
                "Empty. Zero songs. The bar is on the floor and nobody has picked it up. !sr.",
                "Queue status: 404. Songs not found. You know what to do.",
            ],
            hype:
            [
                "THE QUEUE IS EMPTY. LOAD IT UP WITH !sr.",
                "NOTHING QUEUED. CHANGE THAT. !sr NOW.",
                "EMPTY QUEUE ALERT. !sr TO THE RESCUE.",
            ],
            chill: ["queue's empty.", "nothing queued. !sr maybe.", "empty rn."]
        );

        // ── !queue / mine ({user} {queue.mine} {queue.mine.count}) ─────────────
        Add(
            catalog,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.Mine,
            variables: ["user", "queue.mine", "queue.mine.count"],
            informative:
            [
                "{user}'s requests: {queue.mine}",
                "{user} has {queue.mine.count} queued: {queue.mine}",
                "Queued for {user}: {queue.mine}",
            ],
            friendly:
            [
                "Here's where {user}'s songs are: {queue.mine}",
                "{user}, you're in! {queue.mine}",
                "Good news, {user}: {queue.mine}",
            ],
            sassy:
            [
                "{user}, your songs, since you asked so nicely: {queue.mine}",
                "Yes {user}, it's still there. {queue.mine}",
                "{user}'s claim on the speakers: {queue.mine}. Patience.",
            ],
            hype:
            [
                "{user} IS IN THE QUEUE: {queue.mine}",
                "{user}'S BANGERS ARE LOADED: {queue.mine}",
                "GET READY FOR {user}: {queue.mine}",
            ],
            chill:
            [
                "{user}: {queue.mine}",
                "yours, {user}: {queue.mine}",
                "{user}'s up: {queue.mine}",
            ]
        );

        // ── !queue / none ({user}) ─────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.None,
            variables: ["user"],
            informative:
            [
                "{user} has no songs in the queue.",
                "No requests from {user} in the queue.",
                "Nothing queued for {user}.",
            ],
            friendly:
            [
                "{user} hasn't got anything queued yet. !sr is open!",
                "No songs from {user} right now. Add one with !sr!",
                "{user}'s spot is free. Go for it with !sr!",
            ],
            sassy:
            [
                "{user} has exactly zero songs queued. Bold strategy.",
                "Nothing from {user} in there. Can't skip what you never requested.",
                "{user}? Not in the queue. Check again after an !sr.",
            ],
            hype:
            [
                "{user} HAS NOTHING QUEUED. FIX THAT WITH !sr.",
                "NO SONGS FROM {user} YET. !sr AND GET IN THERE.",
                "{user} IS MISSING FROM THE QUEUE. !sr NOW.",
            ],
            chill:
            [
                "nothing from {user}.",
                "{user} has no songs queued.",
                "no requests from {user} rn.",
            ]
        );

        // ── !sr / added ({user} {track.name} {track.artist} {track.link}) ──────
        // {track.link} is a real, directly-fetchable web URL (never Spotify's internal spotify:track:
        // scheme) so chat's own OG-preview resolution turns this line into a real card — art, title,
        // artist — the same as any pasted link, matching the owner's original chat overlay.
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Added,
            variables: ["track.artist", "track.link", "track.name", "user"],
            informative:
            [
                "Added {track.name} by {track.artist} to the queue. {track.link}",
                "Queued: {track.name} by {track.artist}. {track.link}",
                "{track.name} by {track.artist} is in the queue. {track.link}",
            ],
            friendly:
            [
                "Added {track.name} by {track.artist} — great choice! {track.link}",
                "Got it! {track.name} by {track.artist} is queued. {track.link}",
                "{track.name} by {track.artist} coming up — thanks! {track.link}",
            ],
            sassy:
            [
                "Fine. {track.name} by {track.artist} is in the queue. I've queued worse. Barely. {track.link}",
                "Added {track.name} by {track.artist}. Bold. Noted. Logged forever. {track.link}",
                "{track.name} by {track.artist}? Sure. It's in. The queue doesn't judge. I do, but the queue doesn't. {track.link}",
                "{track.name} by {track.artist}, queued. Your taste has been entered into evidence. {track.link}",
            ],
            hype:
            [
                "{track.name} BY {track.artist} IS LOCKED IN. LET'S GO. {track.link}",
                "ADDED {track.name} BY {track.artist}. THE QUEUE JUST GOT BETTER. {track.link}",
                "{track.name} BY {track.artist} INCOMING. BRACE. {track.link}",
            ],
            chill:
            [
                "added {track.name} by {track.artist}. {track.link}",
                "queued {track.name}. nice. {track.link}",
                "{track.name} by {track.artist}, in. {track.link}",
            ]
        );

        // ── !sr / duplicate ({track.name} {track.artist} {requested.by} {user}) ─
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Duplicate,
            variables: ["requested.by", "track.artist", "track.name", "user"],
            informative:
            [
                "\"{track.name}\" is already in the queue (requested by {requested.by}).",
                "\"{track.name}\" is already queued by {requested.by}. Pick a different one and I will add it.",
                "\"{track.name}\" is waiting in the queue already, thanks to {requested.by}.",
            ],
            friendly:
            [
                "Good taste! {requested.by} already queued \"{track.name}\" — pick another and it is yours.",
                "{requested.by} beat you to \"{track.name}\"! Got another in mind?",
                "\"{track.name}\" is already in the queue thanks to {requested.by} — hit me with a different one.",
            ],
            sassy:
            [
                "\"{track.name}\" is ALREADY in the queue. {requested.by} got there first. Try listening before requesting.",
                "Again? {requested.by} already called \"{track.name}\". The queue is not a loop pedal.",
                "Denied. {requested.by} queued \"{track.name}\" already. One copy is plenty, I promise.",
                "I am not queueing \"{track.name}\" twice. {requested.by} beat you to it. Scroll up next time.",
                "Groundbreaking choice — {requested.by} thought of \"{track.name}\" first. Pick something else.",
            ],
            hype:
            [
                "\"{track.name}\" IS ALREADY IN THERE. {requested.by} CALLED IT. GIVE ME ANOTHER BANGER.",
                "{requested.by} ALREADY QUEUED \"{track.name}\". GREAT MINDS. NEXT.",
                "\"{track.name}\" IS LOCKED IN ALREADY — FIND ME A NEW ONE.",
            ],
            chill:
            [
                "\"{track.name}\" is already in the queue. {requested.by} got it.",
                "already queued by {requested.by}. pick another.",
                "{requested.by} already asked for \"{track.name}\".",
            ]
        );

        // ── !sr / alreadyplaying ({user}) ──────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AlreadyPlaying,
            variables: ["track.artist", "track.name", "user"],
            informative:
            [
                "That track is playing right now.",
                "That is the current track.",
                "That one is already playing.",
            ],
            friendly:
            [
                "That is playing right now — enjoy it, then pick the next one!",
                "You are in luck, that one is on already.",
                "Good ears! That is the song currently playing.",
            ],
            sassy:
            [
                "This is LITERALLY the song playing. Right now. In your ears.",
                "It is playing AS WE SPEAK. Requesting it again will not make it play harder.",
                "Bold move requesting the song currently playing. Denied, with affection.",
                "You are requesting the track that is playing. Take a moment.",
            ],
            hype:
            [
                "THAT IS THE SONG PLAYING RIGHT NOW. YOU LOVE IT. WE GET IT.",
                "IT IS ON RIGHT NOW. TURN IT UP INSTEAD.",
                "ALREADY PLAYING. GREAT PICK THOUGH.",
            ],
            chill:
            [
                "that one is playing right now.",
                "it is on already.",
                "already playing, pick another when it ends.",
            ]
        );

        // ── !sr / notfound ({user} {query}) ────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotFound,
            variables: ["query", "user"],
            informative:
            [
                "No tracks found for \"{query}\".",
                "I couldn't find \"{query}\".",
                "Nothing matched \"{query}\".",
            ],
            friendly:
            [
                "Hmm, couldn't find \"{query}\" — try another spelling?",
                "No luck with \"{query}\". Give it another go!",
                "Couldn't find \"{query}\", but don't give up!",
            ],
            sassy:
            [
                "\"{query}\"? Searched everywhere. Even under the couch. Nothing.",
                "Zero results for \"{query}\". Either it doesn't exist or you just invented a song. Impressive either way.",
                "\"{query}\" returned nothing. Spelling is free, you know.",
                "404: \"{query}\" not found. Not on any platform. Possibly not in this reality.",
            ],
            hype:
            [
                "NOTHING FOUND FOR \"{query}\". TRY AGAIN. WE BELIEVE IN YOU.",
                "\"{query}\" CAME BACK EMPTY. RELOAD AND RETRY.",
                "SWING AND A MISS ON \"{query}\". GO AGAIN.",
            ],
            chill:
            [
                "nothing for \"{query}\".",
                "couldn't find \"{query}\". oh well.",
                "no match for \"{query}\".",
            ]
        );

        // ── !skip / skipped ────────────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Skipped,
            variables: [],
            informative: ["Skipped to the next track."],
            friendly:
            [
                "Skipped! On to the next one.",
                "Done — skipped it for you!",
                "Next up! Skipped that one.",
            ],
            sassy:
            [
                "Skipped. Someone had to say it. I just did it.",
                "Gone. We don't talk about that one anymore.",
                "Skipped. The queue thanks you for your service.",
                "That track has been escorted from the building. Next.",
            ],
            hype:
            [
                "SKIPPED. NEXT BANGER INCOMING.",
                "OUT OF HERE. NEXT ONE, LET'S GO.",
                "SKIPPED. ON TO THE HEAT.",
            ],
            chill: ["skipped.", "next one. skipped.", "gone. moving on."]
        );

        // -- !commands / list ({user} {commands} {prefix}) --
        Add(
            catalog,
            BuiltinResponseSlots.Commands.Key,
            BuiltinResponseSlots.Commands.List,
            variables: ["commands", "prefix", "user"],
            informative: ["{commands} — Use {prefix}help <command> for details."],
            friendly:
            [
                "@{user} here's what you can use: {commands} — Use {prefix}help <command> for details.",
                "@{user} happy to help — try one of these: {commands} — Use {prefix}help <command> for details.",
            ],
            sassy:
            [
                "@{user} the commands are: {commands}. Yes, all of them. Use {prefix}help <command> for details, and read the whole thing this time.",
                "@{user} here's every command, since apparently that wasn't obvious: {commands} — Use {prefix}help <command> for details.",
            ],
            hype:
            [
                "@{user} HERE'S THE FULL ARSENAL: {commands} — Use {prefix}help <command> for details.",
            ],
            chill: ["@{user} commands: {commands} — use {prefix}help <command> for details."]
        );

        // -- !commands / empty ({user}) --
        Add(
            catalog,
            BuiltinResponseSlots.Commands.Key,
            BuiltinResponseSlots.Commands.Empty,
            variables: ["user"],
            informative: ["No commands available."],
            friendly: ["@{user} nothing enabled yet — check back soon!"],
            sassy: ["@{user} no commands enabled. It's quiet. Too quiet."],
            hype: ["@{user} NOTHING ENABLED YET. THE STREAMER IS SLEEPING ON THIS."],
            chill: ["@{user} nothing enabled yet."]
        );

        // -- !help <name> / described ({user} {command} {description}) --
        Add(
            catalog,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Described,
            variables: ["command", "description", "user"],
            informative: ["!{command} — {description}"],
            friendly: ["@{user} good question! !{command} — {description}"],
            sassy: ["@{user} !{command} — {description}. You could've read the pins, but sure."],
            hype: ["@{user} !{command} — {description}. NOW GO USE IT."],
            chill: ["@{user} !{command} — {description}"]
        );

        // -- !help / usage ({user} {prefix}) --
        Add(
            catalog,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Usage,
            variables: ["prefix", "user"],
            informative:
            [
                "Use {prefix}help <command> to get help for a specific command, or {prefix}commands to see what's available.",
            ],
            friendly:
            [
                "@{user} use {prefix}help <command> to get help for a specific command, or {prefix}commands to see what's available!",
            ],
            sassy:
            [
                "@{user} it's {prefix}help <command>. Or {prefix}commands if you want the whole list. Not hard.",
            ],
            hype:
            [
                "@{user} USE {prefix}help <command> FOR A SPECIFIC COMMAND, OR {prefix}commands FOR THE FULL ARSENAL.",
            ],
            chill:
            [
                "@{user} {prefix}help <command> for a specific command, or {prefix}commands to see what's available.",
            ]
        );

        // -- !help <name> / unknown ({user} {command} {prefix}) --
        Add(
            catalog,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Unknown,
            variables: ["command", "prefix", "user"],
            informative:
            [
                "Unknown command \"{command}\". Use {prefix}commands to see what's available.",
            ],
            friendly:
            [
                "@{user} hmm, I don't know a command called \"{command}\". Use {prefix}commands to see what's available!",
            ],
            sassy:
            [
                "@{user} \"{command}\"? Never heard of it. Use {prefix}commands to see what's available.",
            ],
            hype: ["@{user} \"{command}\" DOESN'T EXIST. USE {prefix}commands TO SEE WHAT DOES."],
            chill:
            [
                "@{user} no command \"{command}\". use {prefix}commands to see what's available.",
            ]
        );

        // -- !help <name> / no description ({user} {command} {prefix}) --
        Add(
            catalog,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.NoDescription,
            variables: ["command", "prefix", "user"],
            informative:
            [
                "{prefix}{command} — no help text yet. Use {prefix}commands to see what's available.",
            ],
            friendly:
            [
                "@{user} {prefix}{command} exists, but nobody wrote help text for it yet. Use {prefix}commands to see what's available!",
            ],
            sassy:
            [
                "@{user} {prefix}{command} is real, but it comes with no instructions. Use {prefix}commands to see what's available.",
            ],
            hype:
            [
                "@{user} {prefix}{command} EXISTS BUT HAS NO HELP TEXT YET. USE {prefix}commands FOR THE REST.",
            ],
            chill:
            [
                "@{user} {prefix}{command} has no help text yet. use {prefix}commands to see what's available.",
            ]
        );

        // ── !lurk / lurking ({user}) ─────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.Lurking,
            variables: ["user"],
            informative: ["@{user} is now lurking. Enjoy the stream!"],
            friendly: ["@{user} is lurking now — thanks for still being here!"],
            sassy: ["@{user} has entered lurk mode. Silent, watching, judging. Respect."],
            hype: ["@{user} IS LURKING. STILL COUNTS. STILL LEGENDARY."],
            chill: ["@{user} is lurking now."]
        );

        // ── !unlurk / notlurking ({user}) ────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.NotLurking,
            variables: ["user"],
            informative: ["@{user} is no longer lurking. Welcome back!"],
            friendly: ["@{user} is back! Great to see you again!"],
            sassy: ["@{user} has emerged from the shadows. We saw nothing. We assume the worst."],
            hype: ["@{user} IS BACK. THE CHAT IS COMPLETE AGAIN."],
            chill: ["@{user} is back."]
        );

        // ── !accountage / age ({user} {date} {age}) — informative is the legacy sentence verbatim ──
        Add(
            catalog,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.Age,
            variables: ["age", "date", "user"],
            informative: ["Your account was created on {date} ({age} ago)."],
            friendly: ["Your account was created on {date} — {age} ago. Nice!"],
            sassy:
            [
                "Your account was born on {date}. {age} ago, and you're still typing this into chat. Respect the commitment.",
            ],
            hype: ["ACCOUNT CREATED ON {date}. {age} AGO. VETERAN STATUS EARNED."],
            chill: ["account's from {date}, {age} ago."]
        );

        // ── !whisper / usage (no args) ───────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.Usage,
            variables: [],
            informative: ["Usage: !whisper <user> <message>"],
            friendly: ["Almost! Try: !whisper <user> <message>"],
            sassy: ["Usage: !whisper <user> <message>. Both parts. Every time. Not optional."],
            hype: ["USAGE: !whisper <user> <message>. FILL IT IN AND SEND IT."],
            chill: ["usage: !whisper <user> <message>"]
        );

        // ── !whisper / notfound ({user}) ─────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.NotFound,
            variables: ["user"],
            informative: ["Could not find a Twitch user named \"{user}\"."],
            friendly: ["Hmm, couldn't find a Twitch user named \"{user}\" — check the spelling?"],
            sassy: ["\"{user}\" is not a Twitch user. Checked. Twice. Try spelling it right."],
            hype: ["NO TWITCH USER NAMED \"{user}\". DOUBLE-CHECK AND RETRY."],
            chill: ["couldn't find \"{user}\" on twitch."]
        );

        // ── !bansong / nothing (no args) ─────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Nothing,
            variables: [],
            informative: ["No song is currently playing!"],
            friendly: ["Nothing's playing right now, so there's nothing to ban!"],
            sassy: ["Nothing is playing. Banning silence would be a bold new frontier. Let's not."],
            hype: ["NOTHING PLAYING. NOTHING TO BAN. GET A TRACK GOING FIRST."],
            chill: ["nothing playing. nothing to ban."]
        );

        // ── !update / notfound ({user}) ──────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.NotFound,
            variables: ["user"],
            informative: ["Could not find user '{user}' on Twitch."],
            friendly: ["Couldn't find '{user}' on Twitch — mind checking the spelling?"],
            sassy: ["'{user}' does not exist on Twitch. Not my fault. Check the name."],
            hype: ["NO SUCH USER '{user}' ON TWITCH. TRY AGAIN."],
            chill: ["couldn't find '{user}' on twitch."]
        );

        // ── !volume / usage (no args) ────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Usage,
            variables: [],
            informative:
            [
                "Please provide a valid volume level between 0 and 100: !volume <level> (0-100).",
            ],
            friendly: ["Almost! Pick a volume level between 0 and 100: !volume <level> (0-100)."],
            sassy: ["A number. Between zero and a hundred. That's it: !volume <level> (0-100)."],
            hype: ["PICK A NUMBER FROM 0 TO 100 AND SEND IT: !volume <level> (0-100)."],
            chill: ["volume needs a level from 0 to 100: !volume <level> (0-100)."]
        );

        // ── !volume / cannotread (no args) ───────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.CannotRead,
            variables: [],
            informative: ["No song is currently playing!"],
            friendly: ["Nothing's playing, so there's no volume to read or change!"],
            sassy: ["Can't touch the volume of silence. Get a track going first."],
            hype: ["NOTHING PLAYING. NO VOLUME TO TOUCH. START A TRACK FIRST."],
            chill: ["nothing's playing, so no volume."]
        );

        // ── !whisper / twitchunavailable (no args) ───────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.TwitchUnavailable,
            variables: [],
            informative: ["Twitch did not answer just now — try again in a moment."],
            friendly: ["Twitch didn't answer just now — mind trying again in a moment?"],
            sassy: ["Twitch didn't answer. Not my fault. Try again in a moment."],
            hype: ["TWITCH WENT QUIET. TRY AGAIN IN A MOMENT."],
            chill: ["twitch didn't answer — try again in a bit."]
        );

        // ── !whisper / notavailable (no args) ────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.NotAvailable,
            variables: [],
            informative: ["Whispering isn't available right now."],
            friendly: ["Whispering isn't available right now — sorry about that!"],
            sassy: ["Whispering isn't available right now. Take it up with the platform, not me."],
            hype: ["WHISPERING IS DOWN RIGHT NOW. NOTHING TO SEND."],
            chill: ["whispering isn't available right now."]
        );

        // ── !bansong / couldnotban (no args) ─────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.CouldNotBan,
            variables: [],
            informative: ["Could not ban that track — try again in a moment."],
            friendly: ["Couldn't ban that track just now — mind trying again in a moment?"],
            sassy: ["Couldn't ban that track. It lives on. For now. Try again in a moment."],
            hype: ["COULDN'T BAN THAT TRACK. TRY AGAIN IN A MOMENT."],
            chill: ["couldn't ban that track — try again in a bit."]
        );

        // ── !update / twitchunavailable ({user}) ─────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.TwitchUnavailable,
            variables: ["user"],
            informative: ["@{user} Twitch did not answer just now — try again in a moment."],
            friendly: ["@{user} Twitch didn't answer just now — mind trying again in a moment?"],
            sassy: ["@{user} Twitch didn't answer. Not my fault. Try again in a moment."],
            hype: ["@{user} TWITCH WENT QUIET. TRY AGAIN IN A MOMENT."],
            chill: ["@{user} twitch didn't answer — try again in a bit."]
        );

        // ── !update / updatefailed ({user}) ──────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.UpdateFailed,
            variables: ["user"],
            informative: ["Something went wrong updating {user}."],
            friendly: ["Hmm, something went wrong updating {user} — mind trying again?"],
            sassy: ["Something went wrong updating {user}. Not my finest moment. Try again."],
            hype: ["UPDATE FAILED FOR {user}. TRY AGAIN."],
            chill: ["something went wrong updating {user}."]
        );

        // ── !update / loginunresolved ({user}) ───────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.LoginUnresolved,
            variables: ["user"],
            informative: ["@{user} could not resolve your Twitch login."],
            friendly: ["@{user} couldn't figure out your Twitch login there — mind trying again?"],
            sassy: ["@{user} couldn't resolve your Twitch login. That's on you, not me."],
            hype: ["@{user} COULDN'T RESOLVE YOUR TWITCH LOGIN. TRY AGAIN."],
            chill: ["@{user} couldn't resolve your twitch login."]
        );

        // ── !update / owninfoonly ({user}) ───────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.OwnInfoOnly,
            variables: ["user"],
            informative:
            [
                "@{user} you can only update your own info, or be a mod to update others.",
            ],
            friendly:
            [
                "@{user} you can only refresh your own info for now — mods can update others!",
            ],
            sassy:
            [
                "@{user} you can only update your own info. Mods get the extra privilege. You don't.",
            ],
            hype: ["@{user} YOUR OWN INFO ONLY. MODS GET THE REST."],
            chill: ["@{user} you can only update your own info."]
        );

        // ── !coinflip / accountunresolved (no args) ──────────────────────────────
        Add(
            catalog,
            "coinflip",
            BuiltinResponseSlots.Game.AccountUnresolved,
            variables: [],
            informative: ["Could not resolve your account — try again."],
            friendly: ["Couldn't find your account there — mind trying again?"],
            sassy: ["Couldn't resolve your account. Weird. Try again."],
            hype: ["COULDN'T RESOLVE YOUR ACCOUNT. TRY AGAIN."],
            chill: ["couldn't resolve your account — try again."]
        );

        // ── !dice / accountunresolved (no args) ──────────────────────────────────
        Add(
            catalog,
            "dice",
            BuiltinResponseSlots.Game.AccountUnresolved,
            variables: [],
            informative: ["Could not resolve your account — try again."],
            friendly: ["Couldn't find your account there — mind trying again?"],
            sassy: ["Couldn't resolve your account. Weird. Try again."],
            hype: ["COULDN'T RESOLVE YOUR ACCOUNT. TRY AGAIN."],
            chill: ["couldn't resolve your account — try again."]
        );

        // ── !slots / accountunresolved (no args) ─────────────────────────────────
        Add(
            catalog,
            "slots",
            BuiltinResponseSlots.Game.AccountUnresolved,
            variables: [],
            informative: ["Could not resolve your account — try again."],
            friendly: ["Couldn't find your account there — mind trying again?"],
            sassy: ["Couldn't resolve your account. Weird. Try again."],
            hype: ["COULDN'T RESOLVE YOUR ACCOUNT. TRY AGAIN."],
            chill: ["couldn't resolve your account — try again."]
        );

        // ── !sr / disabled (no args) ──────────────────────────────────────────────
        Add(
            catalog,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequestErrors.Disabled,
            variables: [],
            informative: ["This command is currently disabled."],
            friendly: ["This command isn't turned on right now — sorry!"],
            sassy: ["This command is currently disabled. Take it up with the streamer."],
            hype: ["THIS COMMAND IS OFF RIGHT NOW."],
            chill: ["this command's disabled right now."]
        );

        // ── bot status / going offline (shutdown with no successor) — no variables ──
        Add(
            catalog,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.GoingOffline,
            variables: [],
            informative: ["Restarting for an update — back in a moment."],
            friendly: ["Quick restart for an update — I'll be right back!"],
            sassy: ["Going down for a restart. Try not to miss me too much."],
            hype: ["RESTARTING FOR AN UPDATE. BACK IN A FLASH."],
            chill: ["restarting for an update, back in a bit."]
        );

        // ── bot status / Twitch event connection lost + restored — no variables ──
        Add(
            catalog,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.ConnectionLost,
            variables: [],
            informative:
            [
                "⚠️ Lost connection to Twitch events — channel point redeems and commands are paused while I reconnect. Hang tight!",
            ],
            friendly:
            [
                "I lost my link to Twitch events — redeems and commands are paused while I reconnect. Hang tight!",
            ],
            sassy:
            [
                "Twitch dropped my event connection. Redeems and commands are on pause while I sort it out.",
            ],
            hype:
            [
                "LOST THE TWITCH EVENT LINK. REDEEMS AND COMMANDS ARE PAUSED WHILE I RECONNECT. HANG TIGHT!",
            ],
            chill:
            [
                "lost the twitch event link, redeems and commands are paused while i reconnect. hang tight.",
            ]
        );
        Add(
            catalog,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.ConnectionRestored,
            variables: [],
            informative: ["✅ Reconnected! Channel point redeems and commands are working again."],
            friendly: ["I'm back! Redeems and commands are working again."],
            sassy: ["Reconnected. Redeems and commands work again, you're welcome."],
            hype: ["RECONNECTED! REDEEMS AND COMMANDS ARE WORKING AGAIN!"],
            chill: ["reconnected, redeems and commands are working again."]
        );

        return catalog;
    }

    /// <summary>Registers one slot's variables and five tone variation-sets. Every tone is required, keeping the catalog complete.</summary>
    private static void Add(
        Dictionary<(string, string), SlotEntry> catalog,
        string builtinKey,
        string slot,
        string[] informative,
        string[] friendly,
        string[] sassy,
        string[] hype,
        string[] chill,
        string[]? variables = null
    )
    {
        catalog[(builtinKey, slot)] = new SlotEntry(
            variables ?? [],
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [PersonalityTone.Informative] = informative,
                [PersonalityTone.Friendly] = friendly,
                [PersonalityTone.Sassy] = sassy,
                [PersonalityTone.Hype] = hype,
                [PersonalityTone.Chill] = chill,
            }
        );
    }
}
