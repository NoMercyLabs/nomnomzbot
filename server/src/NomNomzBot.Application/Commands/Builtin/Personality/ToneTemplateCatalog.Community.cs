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

/// <summary>Reply content of the community and data-rights built-ins (<c>BuiltinResponseSlots.Community.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddCommunitySlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddForgetmeSlots(catalog);
        AddStatsSlots(catalog);
        AddQuoteSlots(catalog);
        AddVoiceSlots(catalog);
        AddMediaSlots(catalog);
        AddPermitSlots(catalog);
        AddUnpermitSlots(catalog);
    }

    private static void AddCommunitySamples(Dictionary<string, string> samples)
    {
        samples["stats.user"] = "StreamFan42";
        samples["stats.messages"] = "1284";
        samples["stats.watchtime"] = "36h 12m";
        samples["stats.points"] = "5400";
        samples["stats.rank"] = "7";
        samples["stats.streak"] = "12";
        samples["stats.rankpart"] = " (rank #7)";
        samples["stats.streakpart"] = " · 12-stream streak";
        samples["stats.firstseen"] = "2025-03-14";
        samples["quote"] = "#12: \"Never trust a loading bar.\" — StreamFan42 (Elden Ring)";
        samples["quote.number"] = "12";
        samples["voice.id"] = "en-US-AnaNeural";
        samples["voice.name"] = "Ana";
        samples["voice.locale"] = "en-US";
        samples["voice.gender"] = "Female";
        samples["voice.count"] = "3";
        samples["voice.language"] = "en";
        samples["voice.list"] = "Ana, Guy, Jenny";
        samples["voice.languages"] = "EN: en-GB, en-US | NL: nl-NL";
        samples["voice.more"] = "5";
        samples["media.title"] = "Insane 1v4 clutch";
        samples["permit.role"] = "Moderator";
        samples["permit.capability"] = "quotes:write";
    }

    // ── !forgetme / done — the ONLY customizable data-rights line (gdpr-crypto.md section 9 part 1) ──
    private static void AddForgetmeSlots(Dictionary<(string, string), SlotEntry> catalog) =>
        Add(
            catalog,
            BuiltinResponseSlots.Forgetme.Key,
            BuiltinResponseSlots.Forgetme.Done,
            variables: [],
            informative: ["Done — your data's been sanitized, you're a clean slate."],
            friendly: ["All done — your data is wiped and you get a fresh start!"],
            sassy: ["Poof. Gone. You're now a mystery, even to me."],
            hype: ["WIPED CLEAN. FRESH START UNLOCKED."],
            chill: ["all done, you're a clean slate."]
        );

    // ── !stats / !profile ────────────────────────────────────────────────────
    private static void AddStatsSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        // {stats.rankpart} and {stats.streakpart} are precomputed: empty when the viewer has no rank or streak.
        Add(
            catalog,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.Profile,
            variables:
            [
                "stats.firstseen",
                "stats.messages",
                "stats.points",
                "stats.rank",
                "stats.rankpart",
                "stats.streak",
                "stats.streakpart",
                "stats.user",
                "stats.watchtime",
            ],
            informative:
            [
                "{stats.user} · {stats.messages} messages · {stats.watchtime} watched · {stats.points} points{stats.rankpart}{stats.streakpart} · first seen {stats.firstseen}",
            ],
            friendly:
            [
                "{stats.user}, you've sent {stats.messages} messages and earned {stats.points} points — {stats.watchtime} watched together!",
                "Look at {stats.user}: {stats.points} points, {stats.messages} messages, here since {stats.firstseen}!",
                "{stats.user} has been amazing — {stats.watchtime} watched and {stats.points} points!",
            ],
            sassy:
            [
                "CLASSIFIED DOSSIER: {stats.user}. {stats.messages} messages. {stats.watchtime} watched. {stats.points} points. Threat level: chronically online.",
                "{stats.user}: {stats.messages} messages, {stats.points} points, here since {stats.firstseen}. Impressive. Concerning. Both.",
                "{stats.user} has {stats.watchtime} of watch time. I'm not judging. Actually, judging is most of my codebase. I'm judging.",
                "{stats.user} in a nutshell: {stats.messages} messages, {stats.points} points, {stats.watchtime} watched. And somehow, none of it was quiet.",
            ],
            hype:
            [
                "{stats.user}: {stats.points} POINTS, {stats.messages} MESSAGES, {stats.watchtime} WATCHED. LEGEND STATUS.",
                "BIG NUMBERS FOR {stats.user}: {stats.points} POINTS AND {stats.watchtime} WATCHED.",
                "{stats.user} IS BUILT DIFFERENT: {stats.messages} MESSAGES, {stats.points} POINTS.",
            ],
            chill:
            [
                "{stats.user}: {stats.messages} msgs, {stats.watchtime}, {stats.points} pts.",
                "{stats.user} — {stats.points} points, around since {stats.firstseen}.",
                "{stats.user}: {stats.watchtime} watched, {stats.points} pts. solid.",
            ]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.NotSeen,
            variables: ["stats.user"],
            informative: ["I haven't seen {stats.user} chat here yet."],
            friendly: ["I haven't met {stats.user} here yet — say hi soon!"],
            sassy:
            [
                "{stats.user}? Never seen them. Either a ghost or a lurker with a good disguise.",
            ],
            hype: ["NO SIGN OF {stats.user} YET. THE STAGE IS WAITING."],
            chill: ["haven't seen {stats.user} around yet."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.AccountUnresolved,
            variables: [],
            informative: ["stats: your account could not be resolved."],
            friendly: ["I couldn't find your account just now — please try again!"],
            sassy: ["Your account is hiding from me. Try again, and be nicer to it."],
            hype: ["ACCOUNT NOT FOUND. TRY AGAIN, CHAMP."],
            chill: ["couldn't find your account, try again."]
        );
    }

    // ── !quote ───────────────────────────────────────────────────────────────
    private static void AddQuoteSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Quote.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.Show,
            variables: ["quote"],
            informative: ["{quote}"],
            friendly: ["Here's a good one: {quote}"],
            sassy: ["Feast your eyes on this masterpiece: {quote}"],
            hype: ["QUOTE INCOMING: {quote}"],
            chill: ["{quote}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.Empty,
            variables: [],
            informative: ["There are no quotes yet."],
            friendly: ["No quotes saved yet — be the first to add one!"],
            sassy: ["No quotes yet. Nobody here has said anything worth keeping. Shocking."],
            hype: ["NO QUOTES YET. TIME TO MAKE HISTORY."],
            chill: ["no quotes yet."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.NotFound,
            variables: ["quote.number"],
            informative: ["I couldn't find quote #{quote.number}."],
            friendly: ["Hmm, I can't find quote #{quote.number} — maybe try another number?"],
            sassy: ["Quote #{quote.number} doesn't exist. Bold of you to assume it does."],
            hype: ["QUOTE #{quote.number} IS NOWHERE TO BE FOUND. NEXT!"],
            chill: ["no quote #{quote.number}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.Added,
            variables: ["quote"],
            informative: ["Added {quote}"],
            friendly: ["Saved! {quote}"],
            sassy: ["Fine, it's immortalized now. {quote}"],
            hype: ["QUOTE LOCKED IN! {quote}"],
            chill: ["added. {quote}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.AddUsage,
            variables: [],
            informative: ["Usage: !quote add <text> — or reply to a message with !quote add."],
            friendly:
            [
                "To save a quote, type !quote add <text> — or reply to a message with !quote add.",
            ],
            sassy: ["Add what, exactly? !quote add <text>, or reply to a message with !quote add."],
            hype: ["GIVE ME SOMETHING TO SAVE! !QUOTE ADD <TEXT>, OR REPLY TO A MESSAGE."],
            chill: ["!quote add <text>, or reply to a message with !quote add."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.AddFailed,
            variables: [],
            informative: ["I couldn't add that quote."],
            friendly: ["Sorry, I couldn't save that quote — please try again."],
            sassy: ["The quote refused to be saved. Can't blame it."],
            hype: ["THE QUOTE DID NOT SAVE. TRY AGAIN!"],
            chill: ["couldn't add that quote."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.InvalidText,
            variables: [],
            informative: ["A quote needs text, and it can be at most 500 characters."],
            friendly: ["A quote needs some text, up to 500 characters — try again!"],
            sassy: ["A quote is between 1 and 500 characters. Not a novel."],
            hype: ["QUOTES NEED TEXT, MAX 500 CHARACTERS. KEEP IT PUNCHY!"],
            chill: ["quote text: 1 to 500 characters."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.Updated,
            variables: ["quote"],
            informative: ["Updated {quote}"],
            friendly: ["All updated! {quote}"],
            sassy: ["Rewritten. History is written by the editors. {quote}"],
            hype: ["QUOTE UPDATED! {quote}"],
            chill: ["updated. {quote}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.EditUsage,
            variables: [],
            informative: ["Usage: !quote edit <number> <new text>"],
            friendly: ["To change a quote, type !quote edit <number> <new text>."],
            sassy: ["Edit which quote, into what? !quote edit <number> <new text>."],
            hype: ["!QUOTE EDIT <NUMBER> <NEW TEXT> — YOU'VE GOT THIS!"],
            chill: ["!quote edit <number> <new text>"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.EditFailed,
            variables: [],
            informative: ["I couldn't update that quote."],
            friendly: ["Sorry, I couldn't update that quote — please try again."],
            sassy: ["The quote wouldn't budge. Stubborn thing."],
            hype: ["THE UPDATE DID NOT LAND. TRY AGAIN!"],
            chill: ["couldn't update that quote."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.Deleted,
            variables: ["quote.number"],
            informative: ["Deleted quote #{quote.number}."],
            friendly: ["Quote #{quote.number} has been removed."],
            sassy: ["Quote #{quote.number} is gone. We never speak of it again."],
            hype: ["QUOTE #{quote.number} DELETED. GONE FOR GOOD!"],
            chill: ["deleted quote #{quote.number}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.DeleteUsage,
            variables: [],
            informative: ["Usage: !quote del <number>"],
            friendly: ["To remove a quote, type !quote del <number>."],
            sassy: ["Delete which quote? !quote del <number>."],
            hype: ["!QUOTE DEL <NUMBER> — NAME YOUR TARGET!"],
            chill: ["!quote del <number>"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.AccountUnresolved,
            variables: [],
            informative: ["Your account could not be resolved — try again."],
            friendly: ["I couldn't find your account just now — please try again!"],
            sassy: ["Your account is hiding from me. Try again."],
            hype: ["ACCOUNT NOT FOUND. TRY AGAIN, CHAMP."],
            chill: ["couldn't find your account, try again."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.NoAddPermission,
            variables: [],
            informative: ["You don't have permission to add quotes."],
            friendly: ["Sorry, you can't add quotes here — ask the streamer if you'd like to."],
            sassy: ["Adding quotes isn't in your job description."],
            hype: ["QUOTE ADDING IS LOCKED FOR YOU. ASK THE STREAMER!"],
            chill: ["you can't add quotes here."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.NoEditPermission,
            variables: [],
            informative: ["You don't have permission to edit quotes."],
            friendly: ["Sorry, you can't edit quotes here — ask the streamer if you'd like to."],
            sassy: ["Editing quotes isn't in your job description."],
            hype: ["QUOTE EDITING IS LOCKED FOR YOU. ASK THE STREAMER!"],
            chill: ["you can't edit quotes here."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Quote.NoDeletePermission,
            variables: [],
            informative: ["You don't have permission to delete quotes."],
            friendly: ["Sorry, you can't delete quotes here — ask the streamer if you'd like to."],
            sassy: ["Deleting quotes isn't in your job description."],
            hype: ["QUOTE DELETING IS LOCKED FOR YOU. ASK THE STREAMER!"],
            chill: ["you can't delete quotes here."]
        );
    }

    // ── !voice ───────────────────────────────────────────────────────────────
    private static void AddVoiceSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Voice.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Current,
            variables: ["voice.id"],
            informative:
            [
                "Your TTS voice is {voice.id}. Change it with !voice <search>, or !voice clear to use the channel default.",
            ],
            friendly:
            [
                "Your TTS voice is {voice.id}! Want another? Try !voice <search>, or !voice clear for the channel default.",
            ],
            sassy:
            [
                "You sound like {voice.id}. Change it with !voice <search>, or !voice clear if you regret it.",
            ],
            hype:
            [
                "YOUR VOICE IS {voice.id}! SWITCH IT WITH !VOICE <SEARCH>, OR !VOICE CLEAR FOR THE DEFAULT.",
            ],
            chill: ["your voice is {voice.id}. !voice <search> to change, !voice clear to reset."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.CurrentDefault,
            variables: [],
            informative:
            [
                "You're using the channel default TTS voice. Pick your own with !voice <search> — e.g. !voice british female.",
            ],
            friendly:
            [
                "You're on the channel default voice. Pick your own with !voice <search> — like !voice british female!",
            ],
            sassy:
            [
                "You're on the default voice, like everyone else. Stand out with !voice <search> — e.g. !voice british female.",
            ],
            hype:
            [
                "YOU'RE ON THE DEFAULT VOICE! GRAB YOUR OWN WITH !VOICE <SEARCH> — LIKE !VOICE BRITISH FEMALE!",
            ],
            chill:
            [
                "on the default voice. pick one with !voice <search>, e.g. !voice british female.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Cleared,
            variables: [],
            informative: ["Your TTS voice is back to the channel default."],
            friendly: ["Done — you're back on the channel default voice!"],
            sassy: ["Back to the default voice. Basic, but reliable."],
            hype: ["BACK TO THE DEFAULT VOICE! READY FOR THE NEXT ONE."],
            chill: ["back on the default voice."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.ClearFailed,
            variables: [],
            informative: ["I couldn't reset your voice."],
            friendly: ["Sorry, I couldn't reset your voice — please try again."],
            sassy: ["Your voice refuses to be reset. Impressive."],
            hype: ["THE RESET DID NOT WORK. TRY AGAIN!"],
            chill: ["couldn't reset your voice."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Set,
            variables: ["voice.gender", "voice.locale", "voice.name"],
            informative: ["Your TTS voice is now {voice.name} [{voice.locale} {voice.gender}]."],
            friendly: ["All set — your voice is now {voice.name} [{voice.locale} {voice.gender}]!"],
            sassy:
            [
                "Your voice is now {voice.name} [{voice.locale} {voice.gender}]. Try to live up to it.",
            ],
            hype: ["NEW VOICE UNLOCKED: {voice.name} [{voice.locale} {voice.gender}]!"],
            chill: ["voice set to {voice.name} [{voice.locale} {voice.gender}]."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.SetMany,
            variables: ["voice.count", "voice.gender", "voice.locale", "voice.name"],
            informative:
            [
                "Your TTS voice is now {voice.name} [{voice.locale} {voice.gender}]. ({voice.count} matched — add a word to narrow it, or !voice clear to reset.)",
            ],
            friendly:
            [
                "All set — your voice is now {voice.name} [{voice.locale} {voice.gender}]! {voice.count} voices matched, so add a word to narrow it down.",
            ],
            sassy:
            [
                "Your voice is now {voice.name} [{voice.locale} {voice.gender}]. {voice.count} matched, I picked one. You're welcome.",
            ],
            hype:
            [
                "NEW VOICE UNLOCKED: {voice.name} [{voice.locale} {voice.gender}]! {voice.count} MATCHED — ADD A WORD TO NARROW IT!",
            ],
            chill:
            [
                "voice set to {voice.name} [{voice.locale} {voice.gender}]. {voice.count} matched, add a word to narrow it.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.SetFailed,
            variables: [],
            informative: ["I couldn't set that voice."],
            friendly: ["Sorry, I couldn't set that voice — please try again."],
            sassy: ["That voice said no. Try a different one."],
            hype: ["THAT VOICE DID NOT STICK. TRY ANOTHER!"],
            chill: ["couldn't set that voice."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.NoMatch,
            variables: ["query"],
            informative:
            [
                "No voice matched \"{query}\". Try a name, a language like en-US, or an accent like british.",
            ],
            friendly:
            [
                "I couldn't find a voice for \"{query}\". Try a name, a language like en-US, or an accent like british!",
            ],
            sassy:
            [
                "\"{query}\"? No voice sounds like that. Try a name, a language like en-US, or an accent like british.",
            ],
            hype:
            [
                "NO VOICE MATCHED \"{query}\"! TRY A NAME, A LANGUAGE LIKE EN-US, OR AN ACCENT LIKE BRITISH!",
            ],
            chill: ["no voice matched \"{query}\". try a name, en-US, or an accent like british."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Disabled,
            variables: [],
            informative: ["Picking your own voice is turned off on this channel."],
            friendly: ["Picking your own voice is turned off on this channel right now."],
            sassy: ["Voice picking is switched off here. Take it up with the streamer."],
            hype: ["VOICE PICKING IS OFF ON THIS CHANNEL."],
            chill: ["voice picking is off here."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Roulette,
            variables: ["voice.gender", "voice.locale", "voice.name"],
            informative:
            [
                "The wheel has spoken - your voice is now {voice.name} [{voice.locale} {voice.gender}]. No takebacks.",
            ],
            friendly:
            [
                "The wheel picked {voice.name} [{voice.locale} {voice.gender}] for you — enjoy your new voice!",
            ],
            sassy:
            [
                "The wheel has spoken: {voice.name} [{voice.locale} {voice.gender}]. Blame fate, not me.",
            ],
            hype:
            [
                "THE WHEEL HAS SPOKEN: {voice.name} [{voice.locale} {voice.gender}]! NO TAKEBACKS!",
            ],
            chill: ["wheel says {voice.name} [{voice.locale} {voice.gender}]. no takebacks."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Languages,
            variables: ["voice.languages"],
            informative: ["Languages: {voice.languages}"],
            friendly: ["Here are the languages I can speak: {voice.languages}"],
            sassy: ["I'm quite the polyglot: {voice.languages}"],
            hype: ["LANGUAGES UNLOCKED: {voice.languages}"],
            chill: ["languages: {voice.languages}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.Voices,
            variables: ["voice.language", "voice.list"],
            informative:
            [
                "{voice.language} voices: {voice.list}. Pick one with !voice set <name>.",
            ],
            friendly:
            [
                "Voices for {voice.language}: {voice.list}. Pick one with !voice set <name>!",
            ],
            sassy: ["{voice.language} voices: {voice.list}. Choose wisely with !voice set <name>."],
            hype: ["{voice.language} VOICES: {voice.list}. PICK ONE WITH !VOICE SET <NAME>!"],
            chill: ["{voice.language} voices: {voice.list}. !voice set <name> to pick."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.VoicesMore,
            variables: ["voice.language", "voice.list", "voice.more"],
            informative:
            [
                "{voice.language} voices: {voice.list} (+{voice.more} more). Pick one with !voice set <name>.",
            ],
            friendly:
            [
                "Voices for {voice.language}: {voice.list} (and {voice.more} more). Pick one with !voice set <name>!",
            ],
            sassy:
            [
                "{voice.language} voices: {voice.list} (+{voice.more} more, I got tired of typing). Choose with !voice set <name>.",
            ],
            hype:
            [
                "{voice.language} VOICES: {voice.list} (+{voice.more} MORE)! PICK ONE WITH !VOICE SET <NAME>!",
            ],
            chill:
            [
                "{voice.language} voices: {voice.list} (+{voice.more} more). !voice set <name> to pick.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.NoVoicesForLanguage,
            variables: ["voice.language"],
            informative:
            [
                "No voices for {voice.language}. Try !voice languages to see what is available.",
            ],
            friendly:
            [
                "I don't have voices for {voice.language}. Try !voice languages to see what I do have!",
            ],
            sassy: ["{voice.language}? Nothing here. Try !voice languages, it's a short list."],
            hype: ["NO VOICES FOR {voice.language}! CHECK !VOICE LANGUAGES FOR WHAT IS AVAILABLE!"],
            chill: ["no voices for {voice.language}. try !voice languages."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.NoVoices,
            variables: [],
            informative: ["No TTS voices are available right now."],
            friendly: ["I can't find any TTS voices right now — please try again later."],
            sassy: ["The voice catalogue is empty. Everyone is speechless. Literally."],
            hype: ["NO VOICES AVAILABLE RIGHT NOW! CHECK BACK SOON!"],
            chill: ["no tts voices right now."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.GetUsage,
            variables: [],
            informative:
            [
                "Usage: !voice get <language> - e.g. !voice get en, or !voice get en-US.",
            ],
            friendly:
            [
                "To list voices, type !voice get <language> — like !voice get en, or !voice get en-US.",
            ],
            sassy: ["Voices for which language? !voice get <language> — e.g. !voice get en."],
            hype: ["!VOICE GET <LANGUAGE> — LIKE !VOICE GET EN, OR !VOICE GET EN-US!"],
            chill: ["!voice get <language>, e.g. !voice get en."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Voice.SetUsage,
            variables: [],
            informative: ["Usage: !voice set <name> - e.g. !voice set Ana."],
            friendly: ["To pick a voice, type !voice set <name> — like !voice set Ana."],
            sassy: ["Set it to what? !voice set <name> — e.g. !voice set Ana."],
            hype: ["!VOICE SET <NAME> — LIKE !VOICE SET ANA!"],
            chill: ["!voice set <name>, e.g. !voice set Ana."]
        );
    }

    // ── !media ───────────────────────────────────────────────────────────────
    private static void AddMediaSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Media.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Usage,
            variables: [],
            informative: ["Usage: !media <twitch clip or youtube url>"],
            friendly:
            [
                "To share something, type !media followed by a Twitch clip or YouTube link!",
            ],
            sassy: ["Share what? Give me a link. !media <twitch clip or youtube url>"],
            hype: ["DROP A LINK! !MEDIA <TWITCH CLIP OR YOUTUBE URL>"],
            chill: ["!media <twitch clip or youtube url>"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.AccountUnresolved,
            variables: [],
            informative: ["Your account could not be resolved."],
            friendly: ["I couldn't find your account just now — please try again!"],
            sassy: ["Your account is hiding from me. Try again."],
            hype: ["ACCOUNT NOT FOUND. TRY AGAIN, CHAMP."],
            chill: ["couldn't find your account, try again."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Queued,
            variables: ["media.title"],
            informative: ["Added {media.title} to the queue!"],
            friendly: ["{media.title} is in the queue — thanks for sharing!"],
            sassy: ["{media.title} made the queue. Let's hope it earns its spot."],
            hype: ["{media.title} IS IN THE QUEUE! LET'S GO!"],
            chill: ["{media.title} is in the queue."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Pending,
            variables: ["media.title"],
            informative: ["Submitted {media.title} — a mod will review it shortly."],
            friendly: ["Thanks! {media.title} is sent in — a mod will take a look soon."],
            sassy: ["{media.title} is now in the hands of the mods. Cross your fingers."],
            hype: ["{media.title} IS SUBMITTED! THE MODS WILL JUDGE IT SOON!"],
            chill: ["{media.title} submitted, a mod will check it."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Disabled,
            variables: [],
            informative: ["Media share is not enabled on this channel."],
            friendly: ["Media share isn't turned on in this channel right now."],
            sassy: ["Media share is off here. Nobody is watching your clip today."],
            hype: ["MEDIA SHARE IS OFF ON THIS CHANNEL!"],
            chill: ["media share is off here."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.SourceNotAllowed,
            variables: [],
            informative:
            [
                "Only Twitch clips and YouTube videos that this channel accepts can be queued.",
            ],
            friendly:
            [
                "I can only take Twitch clips and YouTube videos this channel accepts — try another link!",
            ],
            sassy: ["That link isn't welcome here. Twitch clips and accepted YouTube videos only."],
            hype: ["ONLY ACCEPTED TWITCH CLIPS AND YOUTUBE VIDEOS CAN BE QUEUED!"],
            chill: ["only accepted twitch clips and youtube videos."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.NotFound,
            variables: [],
            informative: ["I couldn't find that clip or video."],
            friendly: ["I couldn't find that clip or video — check the link and try again!"],
            sassy: ["That clip doesn't exist. Or it's very shy."],
            hype: ["CLIP NOT FOUND! CHECK THE LINK!"],
            chill: ["couldn't find that clip or video."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.TooLong,
            variables: [],
            informative: ["That clip is longer than this channel allows."],
            friendly: ["That one is a bit too long for this channel — try a shorter one!"],
            sassy: ["That's too long. We have places to be. Pick something shorter."],
            hype: ["TOO LONG FOR THE QUEUE! GO FOR A SHORTER ONE!"],
            chill: ["too long for this channel."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.NotEligible,
            variables: [],
            informative: ["You can't submit media on this channel right now."],
            friendly: ["Sorry, you can't submit media here right now."],
            sassy: ["You're not on the list for media submissions."],
            hype: ["MEDIA SUBMISSIONS ARE LOCKED FOR YOU RIGHT NOW!"],
            chill: ["you can't submit media right now."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Cooldown,
            variables: [],
            informative: ["You're submitting too fast — wait a moment before the next one."],
            friendly: ["Slow down a little — wait a moment before your next submission!"],
            sassy: ["Easy there. One clip at a time, please."],
            hype: ["TOO FAST! WAIT A MOMENT BEFORE THE NEXT ONE!"],
            chill: ["too fast, wait a moment."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.QueueFull,
            variables: [],
            informative: ["The media queue is full — try again after some items play."],
            friendly: ["The queue is full right now — try again once some clips have played!"],
            sassy: ["The queue is full. Wait your turn."],
            hype: ["QUEUE FULL! TRY AGAIN AFTER SOME CLIPS PLAY!"],
            chill: ["queue's full, try again later."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Unavailable,
            variables: [],
            informative: ["I couldn't look that up right now — try again in a moment."],
            friendly: ["I couldn't look that up just now — please try again in a moment!"],
            sassy: ["The internet said no. Try again in a moment."],
            hype: ["LOOKUP FAILED! TRY AGAIN IN A MOMENT!"],
            chill: ["couldn't look that up, try again."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Media.Failed,
            variables: [],
            informative: ["I couldn't submit that. Check your points and try again."],
            friendly: ["Sorry, I couldn't submit that — check your points and try again!"],
            sassy: ["Submission failed. Maybe check your wallet."],
            hype: ["SUBMISSION FAILED! CHECK YOUR POINTS AND TRY AGAIN!"],
            chill: ["couldn't submit that. check your points."]
        );
    }

    // ── !permit ──────────────────────────────────────────────────────────────
    private static void AddPermitSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Permit.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.Usage,
            variables: [],
            informative: ["Usage: !permit @user <role|capability> [minutes]"],
            friendly: ["To give someone a permit, type !permit @user <role|capability> [minutes]."],
            sassy: ["Permit who, for what? !permit @user <role|capability> [minutes]"],
            hype: ["!PERMIT @USER <ROLE|CAPABILITY> [MINUTES] — LET'S DELEGATE!"],
            chill: ["!permit @user <role|capability> [minutes]"]
        );

        AddPermitSupportSlots(catalog, key, "permit");

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.GrantedRole,
            variables: ["permit.role", "user"],
            informative: ["Granted {permit.role} to {user}."],
            friendly: ["{user} now has {permit.role}!"],
            sassy: ["{user} is now {permit.role}. Let's hope they can handle it."],
            hype: ["{user} IS NOW {permit.role}! BIG MOVES!"],
            chill: ["gave {permit.role} to {user}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.GrantedCapability,
            variables: ["permit.capability", "user"],
            informative: ["Granted {permit.capability} to {user}."],
            friendly: ["{user} can now use {permit.capability}!"],
            sassy: ["{user} now holds {permit.capability}. Use it wisely."],
            hype: ["{user} GOT {permit.capability}! POWER UP!"],
            chill: ["gave {permit.capability} to {user}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.RoleTooHigh,
            variables: [],
            informative: ["Cannot permit a role above your own level."],
            friendly: ["Sorry, you can only permit roles up to your own level."],
            sassy: ["You can't hand out a role above your own. Nice try."],
            hype: ["THAT ROLE IS ABOVE YOUR LEVEL! NO CAN DO!"],
            chill: ["can't permit a role above your own."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.CapabilityDenied,
            variables: ["permit.capability"],
            informative:
            [
                "You can't permit '{permit.capability}' — it must exist, be delegable, and be one you hold yourself.",
            ],
            friendly:
            [
                "Sorry, I can't permit '{permit.capability}' — it must exist, be delegable, and be one you have yourself.",
            ],
            sassy:
            [
                "'{permit.capability}' isn't yours to give: it must exist, be delegable, and be one you hold.",
            ],
            hype:
            [
                "CAN'T PERMIT '{permit.capability}'! IT MUST EXIST, BE DELEGABLE, AND BE ONE YOU HOLD!",
            ],
            chill:
            [
                "can't permit '{permit.capability}': it must exist, be delegable, and be one you hold.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.GrantFailed,
            variables: [],
            informative: ["The permit could not be granted."],
            friendly: ["Sorry, I couldn't grant that permit — please try again."],
            sassy: ["The permit didn't go through. Not my fault."],
            hype: ["THE PERMIT FAILED! TRY AGAIN!"],
            chill: ["couldn't grant that permit."]
        );
    }

    // ── !unpermit ────────────────────────────────────────────────────────────
    private static void AddUnpermitSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Unpermit.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unpermit.Usage,
            variables: [],
            informative: ["Usage: !unpermit @user [role|capability]"],
            friendly: ["To remove a permit, type !unpermit @user [role|capability]."],
            sassy: ["Unpermit who? !unpermit @user [role|capability]"],
            hype: ["!UNPERMIT @USER [ROLE|CAPABILITY] — TAKE IT BACK!"],
            chill: ["!unpermit @user [role|capability]"]
        );

        AddPermitSupportSlots(catalog, key, "unpermit");

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unpermit.RevokedAll,
            variables: ["user"],
            informative: ["Revoked all permits from {user}."],
            friendly: ["All of {user}'s permits are removed."],
            sassy: ["{user} lost every permit. Ouch."],
            hype: ["ALL PERMITS REVOKED FROM {user}!"],
            chill: ["removed all permits from {user}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unpermit.Revoked,
            variables: ["permit.capability", "user"],
            informative: ["Revoked {permit.capability} from {user}."],
            friendly: ["{permit.capability} is removed from {user}."],
            sassy: ["{user} no longer has {permit.capability}. It was fun while it lasted."],
            hype: ["{permit.capability} REVOKED FROM {user}!"],
            chill: ["removed {permit.capability} from {user}."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Unpermit.RevokeFailed,
            variables: [],
            informative: ["The permit could not be revoked."],
            friendly: ["Sorry, I couldn't revoke that permit — please try again."],
            sassy: ["The permit refuses to leave. Try again."],
            hype: ["THE REVOKE FAILED! TRY AGAIN!"],
            chill: ["couldn't revoke that permit."]
        );
    }

    /// <summary>
    /// The lines <c>!permit</c> and <c>!unpermit</c> share (caller checks and @mention resolution). Both groups
    /// use the same slot names, so the shared support code addresses either group with one set of keys.
    /// </summary>
    private static void AddPermitSupportSlots(
        Dictionary<(string, string), SlotEntry> catalog,
        string key,
        string verb
    )
    {
        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.NotAllowed,
            variables: [],
            informative: [$"{verb}: you are not allowed to manage permits (needs permit:issue)"],
            friendly: ["Sorry, you aren't allowed to manage permits in this channel."],
            sassy: ["Managing permits is above your pay grade."],
            hype: ["PERMITS ARE LOCKED FOR YOU!"],
            chill: ["you can't manage permits here."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.AccountUnresolved,
            variables: [],
            informative: [$"{verb}: your account could not be resolved"],
            friendly: ["I couldn't find your account just now — please try again!"],
            sassy: ["Your account is hiding from me. Try again."],
            hype: ["ACCOUNT NOT FOUND. TRY AGAIN, CHAMP."],
            chill: ["couldn't find your account, try again."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.NoTarget,
            variables: [],
            informative: [$"{verb}: no target — mention a user with @name"],
            friendly: ["Who is it for? Mention a user with @name!"],
            sassy: ["No target. Mention a user with @name."],
            hype: ["NO TARGET! MENTION A USER WITH @NAME!"],
            chill: ["no target, mention a user with @name."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.TargetNotFound,
            variables: ["user"],
            informative: [$"{verb}: '{{user}}' was not found on Twitch"],
            friendly: ["I couldn't find '{user}' on Twitch — check the name!"],
            sassy: ["'{user}' doesn't exist on Twitch. Or hides well."],
            hype: ["'{user}' NOT FOUND ON TWITCH! CHECK THE NAME!"],
            chill: ["'{user}' not found on twitch."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Permit.TargetUnresolved,
            variables: [],
            informative: [$"{verb}: the target user could not be resolved"],
            friendly: ["I couldn't look up that user just now — please try again!"],
            sassy: ["That user slipped through my fingers. Try again."],
            hype: ["COULDN'T RESOLVE THAT USER! TRY AGAIN!"],
            chill: ["couldn't resolve that user."]
        );
    }
}
