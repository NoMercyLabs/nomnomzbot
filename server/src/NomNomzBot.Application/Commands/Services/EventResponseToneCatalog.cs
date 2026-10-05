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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The code-defined chat lines a channel's event response speaks while it follows the platform default, or while
/// its own chat row is on with no text: for each event type that ships ON, and the ad break, five tones of 1–4 varied lines written in that tone's voice (the
/// <c>ToneTemplateCatalog</c> shape, for event responses instead of built-in replies). Every line of an
/// event uses only the placeholders of that event's Informative lines, so a tone never breaks
/// <c>{user}</c>, <c>{count}</c> or <c>{also_said}</c>. The first Informative line of an event is the line
/// the platform seeded before tones existed.
/// </summary>
/// <remarks>
/// Lines are English chat content, like the built-in catalogue — chat is not dashboard copy. Resolution falls
/// back to <see cref="PersonalityTone.Informative"/> for a tone with no lines, and to nothing for an event
/// type the catalogue does not ship.
/// </remarks>
public static class EventResponseToneCatalog
{
    private static readonly IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string[]>
    > Catalog = Build();

    /// <summary>The event types the catalogue ships lines for — the ones that start ON, plus the ad break.</summary>
    public static IReadOnlyList<string> EventTypes { get; } = [.. Catalog.Keys];

    /// <summary>
    /// The lines the bot picks from for <c>(<paramref name="eventType"/>, <paramref name="tone"/>)</c>. Falls
    /// back to Informative when the tone has none; empty when the event type is not in the catalogue.
    /// </summary>
    public static IReadOnlyList<string> Get(string? tone, string eventType)
    {
        if (!Catalog.TryGetValue(eventType, out IReadOnlyDictionary<string, string[]>? tones))
            return [];

        if (
            tones.TryGetValue(PersonalityTone.Normalize(tone), out string[]? lines)
            && lines.Length > 0
        )
            return lines;

        return tones.TryGetValue(PersonalityTone.Informative, out string[]? informative)
            ? informative
            : [];
    }

    /// <summary>One random line for the tone, never the one drawn last time, or null when there is none.</summary>
    public static string? Pick(string? tone, string eventType)
    {
        IReadOnlyList<string> lines = Get(tone, eventType);
        return lines.Count == 0
            ? null
            : NoImmediateRepeatPicker.Pick(
                lines,
                $"event-response:{eventType}:{PersonalityTone.Normalize(tone)}"
            );
    }

    /// <summary>
    /// The lines a row that follows the platform default speaks from: the platform admin's text alone when
    /// there is one, else the catalogue lines for the channel's tone.
    /// </summary>
    public static IReadOnlyList<string> FollowingLines(
        string? platformMessage,
        string? tone,
        string eventType
    ) => string.IsNullOrWhiteSpace(platformMessage) ? Get(tone, eventType) : [platformMessage];

    /// <summary>The first Informative line — the default a channel on the default tone sees first.</summary>
    public static string? FirstInformative(string eventType) =>
        Get(PersonalityTone.Informative, eventType) is { Count: > 0 } lines ? lines[0] : null;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string[]>> Build()
    {
        Dictionary<string, IReadOnlyDictionary<string, string[]>> catalog = new(
            StringComparer.Ordinal
        );

        Add(
            catalog,
            "channel.follow",
            informative:
            [
                "Welcome {user}! Thanks for the follow!",
                "Thanks for following, {user}. Welcome to the channel.",
                "{user} just followed. Welcome!",
            ],
            friendly:
            [
                "Welcome, {user}! So happy you followed. Make yourself at home!",
                "{user} followed! Thank you, you are going to fit right in!",
                "Thanks for the follow, {user}. We are glad you are here!",
            ],
            sassy:
            [
                "{user} followed. Bold of you to commit before seeing the whole stream. Welcome.",
                "Welcome {user}. The follow button is free, but I will pretend it took courage.",
                "{user} is now a follower. No refunds, no returns, lots of chat.",
            ],
            hype:
            [
                "{user} JUST FOLLOWED! WELCOME TO THE SQUAD!",
                "NEW FOLLOWER ALERT! WELCOME, {user}! LET'S GO!",
                "{user} IS IN THE HOUSE! THANKS FOR THE FOLLOW!",
            ],
            chill:
            [
                "welcome {user}, thanks for the follow.",
                "{user} followed. nice to have you.",
                "hey {user}. glad you're here.",
            ]
        );

        Add(
            catalog,
            "channel.subscribe",
            informative:
            [
                "{user} just subscribed! Thank you for the support!",
                "Thank you for subscribing, {user}!",
                "{user} is now a subscriber. Thank you!",
            ],
            friendly:
            [
                "{user} subscribed! Thank you so much, you are wonderful!",
                "Welcome to the family, {user}! Thank you for subscribing!",
                "Thanks for the sub, {user}. It means the world!",
            ],
            sassy:
            [
                "{user} subscribed. Money has changed hands and I am legally required to be grateful. Thank you!",
                "{user} just subbed. Great taste, questionable timing, zero regrets. Thanks!",
                "A sub from {user}! I would clap, but I have no hands. Thank you.",
            ],
            hype:
            [
                "{user} JUST SUBSCRIBED! THANK YOU! THE HYPE IS REAL!",
                "SUB ALERT! {user} IS IN! LET'S GOOO!",
                "{user} SUBBED AND THE CHAT JUST GOT LOUDER! THANK YOU!",
            ],
            chill:
            [
                "{user} subscribed. thanks, appreciate it.",
                "thanks for the sub, {user}.",
                "{user} is a sub now. nice.",
            ]
        );

        Add(
            catalog,
            "channel.subscription.gift",
            informative:
            [
                "{user} gifted {count} sub(s)! How generous!",
                "Thank you {user} for gifting {count} sub(s)!",
                "{user} gave {count} sub(s) to the community. Thank you!",
            ],
            friendly:
            [
                "{user} gifted {count} sub(s)! You are so generous, thank you!",
                "Wow {user}, {count} gifted sub(s). What a kind thing to do!",
                "Thank you {user} for spreading the love with {count} sub(s)!",
            ],
            sassy:
            [
                "{user} gifted {count} sub(s). Some people are just showing off, and honestly, we love it.",
                "{count} gifted sub(s) from {user}. Generosity on this scale is basically bragging. Thanks!",
                "{user} handed out {count} sub(s) like it was free. It was not. Thank you!",
            ],
            hype:
            [
                "{user} GIFTED {count} SUB(S)! ABSOLUTE LEGEND!",
                "{count} GIFTED SUB(S) FROM {user}! THE GENEROSITY IS INSANE!",
                "{user} IS GIVING AWAY {count} SUB(S)! LET'S GOOO!",
            ],
            chill:
            [
                "{user} gifted {count} sub(s). thanks, that's generous.",
                "thanks for the {count} gifted sub(s), {user}.",
                "{user} gave {count} sub(s). big.",
            ]
        );

        Add(
            catalog,
            "channel.subscription.gift.anonymous",
            informative:
            [
                "An anonymous gifter gave {count} sub(s) to the community!",
                "Thank you to our anonymous gifter for {count} sub(s)!",
                "{count} sub(s) gifted anonymously. Thank you!",
            ],
            friendly:
            [
                "A kind anonymous gifter gave {count} sub(s) to the community. Thank you, whoever you are!",
                "Someone anonymous gifted {count} sub(s). Thank you, mystery friend!",
                "{count} sub(s) from a generous secret gifter. What a gift!",
            ],
            sassy:
            [
                "An anonymous gifter dropped {count} sub(s) and vanished. Mysterious. Suspiciously generous. Thank you.",
                "{count} sub(s) from someone who will not give their name. Very cool. Very shady. Thank you!",
                "Anonymous gift: {count} sub(s). We may never know who. The mystery is part of the charm.",
            ],
            hype:
            [
                "AN ANONYMOUS GIFTER JUST DROPPED {count} SUB(S)! WHO ARE YOU?! THANK YOU!",
                "{count} SUB(S) FROM A MYSTERY LEGEND! THANK YOU, WHOEVER YOU ARE!",
                "SECRET GIFTER ALERT! {count} SUB(S) FOR THE COMMUNITY!",
            ],
            chill:
            [
                "an anonymous gifter gave {count} sub(s). thanks, whoever you are.",
                "{count} sub(s) from a mystery gifter. nice.",
                "someone gifted {count} sub(s) quietly. thank you.",
            ]
        );

        Add(
            catalog,
            "channel.subscription.gift.received",
            informative:
            [
                // The old bot's one gifted-sub sentence, kept as the only Informative line so the default speaks it every time.
                "@{user} been gifted a tier {tier} subscription!",
            ],
            friendly:
            [
                "{user} got a gifted tier {tier} sub. Welcome, and enjoy it!",
                "Welcome {user}! A kind gifter just made your day with a tier {tier} sub!",
                "A gifted tier {tier} sub for {user}. How lovely!",
            ],
            sassy:
            [
                "{user} got a gifted tier {tier} sub. Free stuff, and it is not even a trap.",
                "Someone picked {user} for a gifted sub. Tier {tier}, no strings. Lucky day, {user}.",
                "{user} just got a tier {tier} sub for free. Some people just hand out good things. Rude, honestly.",
            ],
            hype:
            [
                "{user} JUST GOT A GIFTED TIER {tier} SUB! LET'S GO!",
                "A GIFTED SUB FOR {user}! WHAT A LEGEND OF A GIFTER!",
                "WELCOME {user}! SOMEONE HOOKED YOU UP WITH A TIER {tier} SUB!",
            ],
            chill:
            [
                "{user} got a gifted tier {tier} sub. enjoy.",
                "a gifted sub for {user}. nice.",
                "{user} got gifted a tier {tier} sub. welcome.",
            ]
        );

        Add(
            catalog,
            "channel.subscription.gift.received.anonymous",
            informative:
            [
                "An anonymous gifter gave a sub to {user}!",
                "{user} received a gifted sub from an anonymous gifter. Enjoy it!",
                "A mystery gifter subscribed {user}. Welcome!",
            ],
            friendly:
            [
                "A kind stranger gifted {user} a sub. Welcome and enjoy!",
                "{user} got a surprise gifted sub from someone anonymous. How lovely!",
                "Someone anonymous just made {user}'s day with a gifted sub!",
            ],
            sassy:
            [
                "An anonymous gifter picked {user} for a sub. No name, no note, just vibes.",
                "{user} got a sub from someone who wants no credit. Suspiciously nice.",
                "A mystery person subbed {user}. Lucky, and probably not a trap.",
            ],
            hype:
            [
                "AN ANONYMOUS GIFTER JUST HOOKED UP {user} WITH A SUB! LET'S GO!",
                "{user} GOT A MYSTERY GIFTED SUB! WELCOME!",
                "SECRET GIFTER STRIKES AGAIN! {user} IS SUBBED!",
            ],
            chill:
            [
                "an anonymous gifter gave {user} a sub. enjoy.",
                "{user} got a mystery sub. nice.",
                "someone gifted {user} a sub quietly.",
            ]
        );

        // {also_said} is a ready-made clause that starts with a space, so it always closes the line.
        Add(
            catalog,
            "channel.subscription.message",
            informative:
            [
                "{user} resubscribed for {months} months! Thank you!{also_said}",
                "Thank you {user} for {months} months of support!{also_said}",
                "{user} is back for month {months}. Thank you!{also_said}",
            ],
            friendly:
            [
                "{user} resubscribed for {months} months. Thank you so much for sticking around!{also_said}",
                "{months} months already, {user}! Thanks for being part of the community!{also_said}",
                "Welcome back, {user}! {months} months of support means the world!{also_said}",
            ],
            sassy:
            [
                "{user} resubscribed for {months} months. At this point they basically live here. Thanks!{also_said}",
                "{months} months, {user}? The commitment is strong. We are honoured and a little concerned.{also_said}",
                "{user} has been here {months} months and keeps coming back. Loyalty or a very sticky habit. Thank you!{also_said}",
            ],
            hype:
            [
                "{user} RESUBSCRIBED FOR {months} MONTHS! ABSOLUTE LEGEND!{also_said}",
                "{months} MONTHS AND STILL GOING! THANK YOU {user}!{also_said}",
                "{user} IS BACK FOR {months} MONTHS! THE LOYALTY IS INSANE!{also_said}",
            ],
            chill:
            [
                "{user} resubscribed for {months} months. thanks.{also_said}",
                "{months} months already, {user}. appreciate it.{also_said}",
                "thanks for {months} months, {user}.{also_said}",
            ]
        );

        Add(
            catalog,
            "channel.cheer",
            informative:
            [
                "{user} cheered {bits} bits! Thank you!",
                "Thank you {user} for the {bits} bits!",
                "{user} cheered with {bits} bits. Thank you for the support!",
            ],
            friendly:
            [
                "{user} cheered {bits} bits. Thank you so much!",
                "Wow, {bits} bits from {user}! You are the best!",
                "Thank you {user} for {bits} bits. You are so kind!",
            ],
            sassy:
            [
                "{user} cheered {bits} bits. Actual money for pixels. I respect it. Thank you.",
                "{bits} bits from {user}. The economy of tiny animated things continues. Thank you!",
                "{user} threw {bits} bits our way. Correct behaviour. Thanks!",
            ],
            hype:
            [
                "{user} CHEERED {bits} BITS! LET'S GOOO!",
                "{bits} BITS FROM {user}! THE CHAT IS SHAKING!",
                "{user} JUST DROPPED {bits} BITS! ABSOLUTE LEGEND!",
            ],
            chill:
            [
                "{user} cheered {bits} bits. thanks.",
                "{bits} bits from {user}. appreciate it.",
                "thanks for the bits, {user}.",
            ]
        );

        Add(
            catalog,
            "channel.raid",
            informative:
            [
                "{user} is raiding with {viewers} viewers! Welcome raiders!",
                "Welcome raiders! {user} brought {viewers} viewers.",
                "Incoming raid from {user} with {viewers} viewers. Welcome!",
            ],
            friendly:
            [
                "{user} raided with {viewers} viewers. Welcome, everyone, make yourselves at home!",
                "Welcome raiders! Thank you {user} for bringing {viewers} friends!",
                "{viewers} new faces from {user}'s raid. So glad you are here!",
            ],
            sassy:
            [
                "{user} raided with {viewers} viewers. Chat, act natural. Raiders, the good chairs are taken. Welcome anyway.",
                "{viewers} raiders from {user}. Please look busy. Welcome!",
                "Here comes {user} with {viewers} viewers. We were not ready. Welcome anyway.",
            ],
            hype:
            [
                "{user} IS RAIDING WITH {viewers} VIEWERS! WELCOME RAIDERS! LET'S GOOO!",
                "RAID INCOMING FROM {user}! {viewers} NEW FRIENDS! MAKE SOME NOISE!",
                "{viewers} RAIDERS FROM {user}! THE CHAT JUST EXPLODED! WELCOME!",
            ],
            chill:
            [
                "{user} raided with {viewers} viewers. welcome, raiders.",
                "welcome raiders. thanks {user} for the {viewers}.",
                "{viewers} raiders from {user}. hey all.",
            ]
        );

        Add(
            catalog,
            "channel.poll.end",
            informative:
            [
                "\U0001F4CA Poll ended: \"{poll.title}\" \u2014 Winner: {poll.winner}{poll.winner.percentage} | {poll.results}",
            ],
            friendly:
            [
                "The poll \"{poll.title}\" is done! {poll.winner} won{poll.winner.percentage}. Thank you all for voting!",
                "Thanks for voting! \"{poll.title}\" went to {poll.winner}{poll.winner.percentage}.",
            ],
            sassy:
            [
                "\"{poll.title}\" is settled. {poll.winner} won{poll.winner.percentage}. The rest of you can sulk quietly.",
                "{poll.winner} takes the poll \"{poll.title}\"{poll.winner.percentage}. Democracy has spoken.",
            ],
            hype:
            [
                "THE POLL IS IN! {poll.winner} WINS \"{poll.title}\"{poll.winner.percentage}! LET'S GOOO!",
                "{poll.winner} TAKES THE POLL{poll.winner.percentage}! THANK YOU FOR VOTING!",
            ],
            chill:
            [
                "poll's done. {poll.winner} won{poll.winner.percentage}.",
                "\"{poll.title}\" went to {poll.winner}{poll.winner.percentage}. thanks for voting.",
            ]
        );

        Add(
            catalog,
            "channel.ban",
            informative:
            [
                "@{user} has been banned from the channel. Reason: {reason}",
                "@{user} has been banned from the channel by {moderator}. Duration: {duration}. Reason: {reason}",
            ],
            friendly:
            [
                "{user} has been banned from the channel ({duration}). Reason: {reason}",
                "{moderator} has had to ban {user} ({duration}). Reason: {reason}",
            ],
            sassy:
            [
                "{user} has been shown the door by {moderator} ({duration}). Reason: {reason}",
                "Bye {user}. {moderator} says it is {duration}. Reason: {reason}",
            ],
            hype:
            [
                "{user} HAS BEEN BANNED BY {moderator}! DURATION: {duration}! REASON: {reason}",
                "BANHAMMER! {user} IS GONE ({duration})! REASON: {reason}",
            ],
            chill:
            [
                "{user} has been banned ({duration}). reason: {reason}",
                "{moderator} banned {user}. {duration}. {reason}",
            ]
        );

        Add(
            catalog,
            "channel.unban",
            informative:
            [
                "@{user} has been unbanned from the channel.",
                "@{user} has been unbanned by {moderator}.",
            ],
            friendly:
            [
                "{user} has been unbanned. Welcome back!",
                "{moderator} unbanned {user}. A fresh start!",
            ],
            sassy:
            [
                "{user} is unbanned. Try to behave this time.",
                "{moderator} unbanned {user}. We will be watching.",
            ],
            hype:
            [
                "{user} IS UNBANNED! WELCOME BACK!",
                "{moderator} UNBANNED {user}! THE LEGEND RETURNS!",
            ],
            chill: ["{user} is unbanned. welcome back.", "{moderator} unbanned {user}."]
        );

        Add(
            catalog,
            "channel.moderator.add",
            informative:
            [
                "@{user} has been added as a moderator in the channel.",
                "@{user} is now a moderator in the channel.",
            ],
            friendly:
            [
                "{user} is now a moderator. Thank you for helping out!",
                "Welcome to the mod team, {user}!",
            ],
            sassy:
            [
                "{user} is now a moderator. Do try not to enjoy the power too much.",
                "{user} got the sword. Chat, behave.",
            ],
            hype: ["{user} IS NOW A MODERATOR! LET'S GOOO!", "NEW MOD ALERT! WELCOME {user}!"],
            chill: ["{user} is now a moderator.", "welcome to the mod team, {user}."]
        );

        Add(
            catalog,
            "channel.moderator.remove",
            informative:
            [
                "@{user} has been removed as a moderator in the channel.",
                "@{user} is no longer a moderator in the channel.",
            ],
            friendly:
            [
                "{user} is no longer a moderator. Thank you for your help!",
                "Thanks for modding, {user}!",
            ],
            sassy:
            [
                "{user} has been relieved of their moderator duties.",
                "{user} handed back the sword.",
            ],
            hype:
            [
                "{user} IS NO LONGER A MODERATOR! THANK YOU FOR YOUR SERVICE!",
                "MOD DUTY OVER FOR {user}! RESPECT!",
            ],
            chill: ["{user} is no longer a moderator.", "thanks for modding, {user}."]
        );

        // The ad-break default ships off; these lines speak for a channel that turns its own row on and leaves
        // the text empty. {user} is empty on an automatic break, so the lines use only the duration.
        Add(
            catalog,
            "channel.ad_break.begin",
            informative: ["An ad break has started for {ad.duration}. Please stay tuned!"],
            friendly:
            [
                "Quick ad break for {ad.duration}! Grab a drink and stretch, we will see you soon!",
                "Ads for {ad.duration}, friends. Thank you for sticking around!",
                "Ads are on for {ad.duration}. Go hydrate, we will be right here!",
            ],
            sassy:
            [
                "Ads for {ad.duration}. Yes, again. Go stretch before your spine files a complaint.",
                "The ads are here for {ad.duration}. Blame capitalism, not me.",
                "{ad.duration} of ads. Perfect time to drink some water, since you clearly forgot.",
                "Ad break, {ad.duration}. Do not even think about leaving, I will know.",
            ],
            hype:
            [
                "AD BREAK FOR {ad.duration}! STRETCH, HYDRATE, COME BACK STRONGER!",
                "{ad.duration} OF ADS! GET UP, GET MOVING, WE ARE BACK SOON!",
                "ADS FOR {ad.duration}! DON'T YOU DARE LEAVE, THE HYPE CONTINUES AFTER!",
            ],
            chill:
            [
                "ads for {ad.duration}. good time to stretch.",
                "short ad break, {ad.duration}. we'll be here.",
                "ads rolling for {ad.duration}. grab some water.",
            ]
        );

        // The end of the break, said once its duration passed and the stream is still live. Only {ad.duration} is set.
        Add(
            catalog,
            "channel.ad_break.end",
            informative: ["The ad break has ended. Thanks for your patience!"],
            friendly:
            [
                "The ads are done, friends. Thank you for waiting with us!",
                "We are back! Thanks for sticking around through the ads.",
            ],
            sassy:
            [
                "The ads are over. You survived. I am proud of you.",
                "Ads done. Welcome back, I knew you would not leave.",
            ],
            hype:
            [
                "ADS ARE OVER! WE ARE BACK! THANK YOU FOR STAYING!",
                "THE ADS ARE GONE! LET'S GO!",
            ],
            chill: ["ads are done. thanks for waiting.", "we're back. thanks for hanging on."]
        );

        // The warning before the next ad, said once at about 3 minutes. {ad.when} reads "in ~3 minutes" and
        // {ad.seconds} is the break length in seconds.
        Add(
            catalog,
            "channel.ad_break.upcoming",
            informative:
            [
                "Heads up: an ad break is coming {ad.when} ({ad.seconds} seconds long). Subscribers skip ads.",
            ],
            friendly:
            [
                "Friends, a short ad break is coming {ad.when} ({ad.seconds} seconds). Subscribers skip ads!",
                "Quick heads up: ads {ad.when} for {ad.seconds} seconds. Subs do not see them!",
            ],
            sassy:
            [
                "Ads are coming {ad.when} for {ad.seconds} seconds. Subscribers skip them. Just saying.",
                "{ad.seconds} seconds of ads {ad.when}. Subs get to skip. Think about it.",
            ],
            hype:
            [
                "AD BREAK COMING {ad.when}! {ad.seconds} SECONDS! SUBSCRIBERS SKIP THE ADS!",
                "ADS {ad.when} FOR {ad.seconds} SECONDS! SUBS SKIP THEM!",
            ],
            chill:
            [
                "ads coming {ad.when}, {ad.seconds} seconds. subs skip them.",
                "heads up, ad break {ad.when} for {ad.seconds} seconds.",
            ]
        );

        return catalog;
    }

    /// <summary>Registers one event type's five tones. Every tone is required, keeping the catalogue complete.</summary>
    private static void Add(
        Dictionary<string, IReadOnlyDictionary<string, string[]>> catalog,
        string eventType,
        string[] informative,
        string[] friendly,
        string[] sassy,
        string[] hype,
        string[] chill
    ) =>
        catalog[eventType] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PersonalityTone.Informative] = informative,
            [PersonalityTone.Friendly] = friendly,
            [PersonalityTone.Sassy] = sassy,
            [PersonalityTone.Hype] = hype,
            [PersonalityTone.Chill] = chill,
        };
}
