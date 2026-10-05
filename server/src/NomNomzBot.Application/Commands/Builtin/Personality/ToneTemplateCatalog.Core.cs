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

/// <summary>Reply content of the core chat built-ins and the bot's own lines (<c>BuiltinResponseSlots.Core.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    /// <summary>The three instant games; each is its own reply group, keyed by its chat trigger word.</summary>
    private static readonly string[] GameKeys = ["coinflip", "dice", "slots"];

    private static void AddCoreSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddLurkAndAccountAgeSlots(catalog);
        AddFollowAgeSlots(catalog);
        AddWhisperAndUpdateSlots(catalog);
        AddDiscordSlots(catalog);
        AddLeaderboardSlots(catalog);
        AddSystemSlots(catalog);
        AddRewardSlots(catalog);
        foreach (string gameKey in GameKeys)
            AddGameSlots(catalog, gameKey);
    }

    private static void AddLurkAndAccountAgeSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.AccountUnresolved,
            variables: ["user"],
            informative: ["@{user} your account could not be resolved."],
            friendly: ["@{user} I couldn't find your account there — mind trying again?"],
            sassy: ["@{user} your account could not be resolved. Awkward. Try again."],
            hype: ["@{user} COULDN'T RESOLVE YOUR ACCOUNT. TRY AGAIN."],
            chill: ["@{user} couldn't resolve your account."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.AccountUnresolved,
            variables: ["user"],
            informative: ["@{user} your account could not be resolved."],
            friendly: ["@{user} I couldn't find your account there — mind trying again?"],
            sassy: ["@{user} your account could not be resolved. Awkward. Try again."],
            hype: ["@{user} COULDN'T RESOLVE YOUR ACCOUNT. TRY AGAIN."],
            chill: ["@{user} couldn't resolve your account."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.TwitchUnavailable,
            variables: ["user"],
            informative: ["@{user} Twitch did not answer just now — try again in a moment."],
            friendly: ["@{user} Twitch didn't answer just now — mind trying again in a moment?"],
            sassy: ["@{user} Twitch didn't answer. Not my fault. Try again in a moment."],
            hype: ["@{user} TWITCH WENT QUIET. TRY AGAIN IN A MOMENT."],
            chill: ["@{user} twitch didn't answer — try again in a bit."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.NotFound,
            variables: ["user"],
            informative: ["@{user} could not find your account on Twitch."],
            friendly: ["@{user} I couldn't find your account on Twitch — odd! Try again later?"],
            sassy: ["@{user} Twitch says you don't exist. Bold claim. Try again later."],
            hype: ["@{user} NO ACCOUNT FOUND ON TWITCH. TRY AGAIN LATER."],
            chill: ["@{user} couldn't find your account on twitch."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.Undetermined,
            variables: ["user"],
            informative: ["@{user} your account age could not be determined."],
            friendly: ["@{user} I couldn't work out your account age just now — sorry!"],
            sassy: ["@{user} your account age is a mystery. Even to me."],
            hype: ["@{user} ACCOUNT AGE UNKNOWN. THE MYSTERY DEEPENS."],
            chill: ["@{user} couldn't work out your account age."]
        );
    }

    private static void AddFollowAgeSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.Age,
            variables: ["age", "user"],
            informative: ["You have been following for {age}!"],
            friendly: ["You've been following for {age} — thank you for sticking around!"],
            sassy: ["You have been following for {age}. That's a long time to keep showing up."],
            hype: ["YOU HAVE BEEN FOLLOWING FOR {age}! LEGEND STATUS!"],
            chill: ["you've been following for {age}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.NotFollowing,
            variables: ["user"],
            informative: ["You are not following!"],
            friendly: ["You're not following yet — hit that follow button whenever you like!"],
            sassy: ["You are not following. Bold of you to ask. The button is right there."],
            hype: ["YOU ARE NOT FOLLOWING! FIX THAT RIGHT NOW!"],
            chill: ["you're not following yet."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.TwitchUnavailable,
            variables: ["user"],
            informative: ["Twitch did not answer just now — try again in a moment."],
            friendly: ["Twitch didn't answer just now — mind trying again in a moment?"],
            sassy: ["Twitch didn't answer. Not my fault. Try again in a moment."],
            hype: ["TWITCH WENT QUIET. TRY AGAIN IN A MOMENT."],
            chill: ["twitch didn't answer — try again in a bit."]
        );
    }

    private static void AddWhisperAndUpdateSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.Sent,
            variables: ["user"],
            informative: ["Whispered {user}."],
            friendly: ["Sent your whisper to {user}!"],
            sassy: ["Whispered {user}. Secrets are safe. Probably."],
            hype: ["WHISPER DELIVERED TO {user}."],
            chill: ["whispered {user}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.SendFailed,
            variables: ["user"],
            informative: ["Could not whisper {user}."],
            friendly: ["Couldn't whisper {user} just now — sorry about that!"],
            sassy: ["Could not whisper {user}. They may not want to hear from us."],
            hype: ["WHISPER TO {user} FAILED. TRY AGAIN."],
            chill: ["couldn't whisper {user}."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.Updated,
            variables: ["user"],
            informative: ["Updated user info for {user}!"],
            friendly: ["All done — {user}'s info is fresh!"],
            sassy: ["Updated {user}'s info. You're welcome."],
            hype: ["{user}'S INFO IS UPDATED. FRESH AND READY."],
            chill: ["updated {user}'s info."]
        );
    }

    private static void AddDiscordSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.Invite,
            variables: ["discord.invite"],
            informative: ["Join the community on Discord: {discord.invite}"],
            friendly: ["Come hang out with us on Discord: {discord.invite}"],
            sassy: ["We have a Discord. Yes, really. Come on in: {discord.invite}"],
            hype: ["JOIN THE DISCORD! THE PARTY NEVER STOPS: {discord.invite}"],
            chill: ["discord's here if you want it: {discord.invite}"]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.NotConnected,
            variables: [],
            informative: ["This channel doesn't have a Discord server linked yet."],
            friendly: ["This channel doesn't have a Discord server linked yet — maybe soon!"],
            sassy: ["No Discord server linked. Somebody has homework."],
            hype: ["NO DISCORD LINKED YET. THE STREAMER HAS HOMEWORK."],
            chill: ["no discord linked here yet."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.Unavailable,
            variables: [],
            informative: ["The Discord invite isn't available right now — try again in a moment."],
            friendly: ["The Discord invite isn't ready right now — mind trying again in a moment?"],
            sassy: ["The Discord invite is being difficult. Try again in a moment."],
            hype: ["DISCORD INVITE IS DOWN RIGHT NOW. TRY AGAIN IN A MOMENT."],
            chill: ["discord invite's not available right now, try again in a bit."]
        );
    }

    private static void AddLeaderboardSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.None,
            variables: [],
            informative: ["No leaderboard is configured for this channel yet."],
            friendly: ["This channel doesn't have a leaderboard set up yet — check back soon!"],
            sassy: ["No leaderboard exists. Nobody is winning. Nobody is losing."],
            hype: ["NO LEADERBOARD YET. THE RACE HASN'T STARTED."],
            chill: ["no leaderboard set up yet."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.Empty,
            variables: [],
            informative: ["The leaderboard doesn't have any ranked entries yet."],
            friendly: ["Nobody is on the leaderboard yet — you could be first!"],
            sassy: ["The leaderboard is empty. Somebody go be impressive."],
            hype: ["EMPTY LEADERBOARD. THE TOP SPOT IS WIDE OPEN."],
            chill: ["nobody's on the leaderboard yet."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.Top,
            variables: ["leaderboard.metric", "leaderboard.list", "leaderboard.count"],
            informative: ["Top {leaderboard.metric}: {leaderboard.list}"],
            friendly:
            [
                "Here are our top {leaderboard.metric} — great job everyone! {leaderboard.list}",
            ],
            sassy: ["Top {leaderboard.metric}, for those keeping score: {leaderboard.list}"],
            hype: ["TOP {leaderboard.metric}! THE LEGENDS: {leaderboard.list}"],
            chill: ["top {leaderboard.metric}: {leaderboard.list}"]
        );
    }

    private static void AddRewardSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.Reward.Key,
            BuiltinResponseSlots.Reward.NoPermission,
            variables: ["user"],
            informative:
            [
                "@{user}, you don't have permission to use this reward. Your points have been refunded.",
            ],
            friendly:
            [
                "@{user}, sorry, this reward isn't available to you. Your points have been refunded!",
            ],
            sassy: ["@{user}, that reward isn't for you. Your points are back, no harm done."],
            hype: ["@{user}, THAT REWARD ISN'T UNLOCKED FOR YOU! POINTS REFUNDED!"],
            chill: ["@{user}, that reward isn't for you. points refunded."]
        );
    }

    private static void AddSystemSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Add(
            catalog,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.PermissionDenied,
            variables: [],
            informative: ["You don't have permission to use that command."],
            friendly: ["Sorry, that command isn't available to you right now."],
            sassy: ["You don't have permission for that command. Nice try, though."],
            hype: ["THAT COMMAND ISN'T FOR YOU. YET."],
            chill: ["you can't use that command."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.Cooldown,
            variables: [],
            informative: ["That command is still on cooldown."],
            friendly: ["That command needs a short rest — try again in a moment!"],
            sassy: ["That command is still on cooldown. Patience is a virtue."],
            hype: ["COOLDOWN ACTIVE. CHARGING UP FOR NEXT TIME."],
            chill: ["that command's on cooldown."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.Failed,
            variables: [],
            informative: ["Sorry, that command hit a snag and didn't finish."],
            friendly: ["Oops, that command hit a snag and didn't finish — sorry!"],
            sassy: ["That command hit a snag and gave up. Not my finest moment."],
            hype: ["THAT COMMAND HIT A SNAG. IT DIDN'T FINISH."],
            chill: ["that command hit a snag and didn't finish."]
        );

        Add(
            catalog,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.NothingRan,
            variables: [],
            informative: ["That command has nothing set up to run yet."],
            friendly: ["That command isn't set up yet — the streamer may still be working on it!"],
            sassy: ["That command has nothing behind it. It's all show."],
            hype: ["THAT COMMAND HAS NOTHING TO RUN YET. COMING SOON."],
            chill: ["that command isn't set up yet."]
        );
    }

    /// <summary>The reply cases of one instant game, authored once and registered under the game's own trigger key.</summary>
    private static void AddGameSlots(Dictionary<(string, string), SlotEntry> catalog, string key)
    {
        string[] name = ["game.name"];

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.Usage,
            variables: name,
            informative: ["Usage: !{game.name} <bet>"],
            friendly: ["Almost! Try: !{game.name} <bet>"],
            sassy: ["Usage: !{game.name} <bet>. A number. Above zero. That's it."],
            hype: ["USAGE: !{game.name} <bet>. PICK A NUMBER AND GO."],
            chill: ["usage: !{game.name} <bet>"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.NotEnabled,
            variables: name,
            informative:
            [
                "!{game.name} isn't enabled yet — turn it on under Economy → Games. Games spend your channel currency, so set that up there too.",
            ],
            friendly:
            [
                "!{game.name} isn't turned on yet — the streamer can enable it under Economy → Games, along with the channel currency.",
            ],
            sassy:
            [
                "!{game.name} isn't enabled. Someone has to switch it on under Economy → Games, and set up the currency too.",
            ],
            hype:
            [
                "!{game.name} IS OFF FOR NOW. TURN IT ON UNDER ECONOMY → GAMES AND SET UP THE CURRENCY.",
            ],
            chill:
            [
                "!{game.name} isn't on yet. enable it under economy → games, plus the currency.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.Unavailable,
            variables: [],
            informative: ["Games are unavailable right now."],
            friendly: ["Games aren't available right now — sorry about that!"],
            sassy: ["Games are unavailable. The house is closed. Try again later."],
            hype: ["GAMES ARE DOWN RIGHT NOW. TRY AGAIN SOON."],
            chill: ["games aren't available right now."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.Won,
            variables: ["game.name", "game.bet", "game.payout", "game.balance"],
            informative:
            [
                "You won {game.payout} on {game.name} (bet {game.bet})! Balance: {game.balance}",
            ],
            friendly:
            [
                "Congratulations, you won {game.payout} on {game.name}! Your balance is now {game.balance}.",
            ],
            sassy:
            [
                "You won {game.payout} on {game.name}. Luck, not skill. Balance: {game.balance}",
            ],
            hype: ["YOU WON {game.payout} ON {game.name}! BALANCE: {game.balance}. LEGEND."],
            chill: ["won {game.payout} on {game.name}. balance: {game.balance}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.Lost,
            variables: ["game.name", "game.bet", "game.balance"],
            informative: ["You lost {game.bet} on {game.name}. Balance: {game.balance}"],
            friendly:
            [
                "Aw, you lost {game.bet} on {game.name}. Better luck next time! Balance: {game.balance}",
            ],
            sassy:
            [
                "You lost {game.bet} on {game.name}. The house says thanks. Balance: {game.balance}",
            ],
            hype: ["LOST {game.bet} ON {game.name}. COMEBACK TIME. BALANCE: {game.balance}"],
            chill: ["lost {game.bet} on {game.name}. balance: {game.balance}"]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.BetOutOfRange,
            variables: [],
            informative: ["Bet is outside the allowed range."],
            friendly: ["That bet is outside the allowed range — try a different amount!"],
            sassy: ["That bet is outside the allowed range. Read the limits."],
            hype: ["BET OUT OF RANGE. PICK AN AMOUNT WITHIN THE LIMITS."],
            chill: ["that bet's outside the allowed range."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.InsufficientFunds,
            variables: [],
            informative: ["Insufficient funds."],
            friendly: ["You don't have enough for that bet — try a smaller one!"],
            sassy: ["You can't afford that bet. Dream smaller."],
            hype: ["NOT ENOUGH FUNDS FOR THAT BET. GO EARN SOME MORE."],
            chill: ["not enough funds for that bet."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.AgeConsentRequired,
            variables: [],
            informative: ["This game requires confirming you are 18 or older."],
            friendly: ["To play this game, please confirm you are 18 or older first."],
            sassy: ["This game needs you to confirm you are 18 or older. Rules are rules."],
            hype: ["CONFIRM YOU ARE 18 OR OLDER TO PLAY THIS GAME."],
            chill: ["you need to confirm you're 18 or older to play this."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.NotAllowed,
            variables: [],
            informative: ["Insufficient role to play this game."],
            friendly: ["This game isn't open to your role yet — sorry!"],
            sassy: ["Your role isn't high enough for this game. Climb the ladder."],
            hype: ["YOUR ROLE ISN'T HIGH ENOUGH FOR THIS GAME YET."],
            chill: ["your role can't play this game yet."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.OnCooldown,
            variables: [],
            informative: ["Game is on cooldown."],
            friendly: ["This game needs a short rest — try again in a moment!"],
            sassy: ["Game is on cooldown. The house needs a breather."],
            hype: ["GAME ON COOLDOWN. RELOAD AND COME BACK."],
            chill: ["game's on cooldown."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.StreamLimit,
            variables: [],
            informative: ["Per-stream play limit reached for this game."],
            friendly:
            [
                "You've played this game as much as allowed this stream — see you next time!",
            ],
            sassy: ["Per-stream play limit reached. The house has seen enough of you today."],
            hype: ["PLAY LIMIT REACHED FOR THIS STREAM. COME BACK NEXT TIME."],
            chill: ["play limit reached for this stream."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.CurrencyDisabled,
            variables: [],
            informative: ["Currency is disabled."],
            friendly: ["The channel currency isn't turned on yet, so there's nothing to bet."],
            sassy: ["No channel currency is enabled. There is nothing to gamble."],
            hype: ["NO CHANNEL CURRENCY YET. NOTHING TO BET."],
            chill: ["the channel currency isn't on yet."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.Game.PlayFailed,
            variables: [],
            informative: ["That didn't work — try again."],
            friendly: ["Hmm, that didn't work — mind trying again?"],
            sassy: ["That didn't work. Try again. Slower, maybe."],
            hype: ["THAT DIDN'T WORK. TRY AGAIN."],
            chill: ["that didn't work, try again."]
        );
    }

    private static void AddCoreSamples(Dictionary<string, string> samples)
    {
        samples["user"] = "StreamFan42";
        samples["uptime"] = "2 hours 14 minutes";
        samples["commands"] = "!uptime, !song, !sr, !lurk";
        samples["prefix"] = "!";
        samples["command"] = "!socials";
        samples["description"] = "Links to all my socials.";
        samples["age"] = "3 years 2 months";
        samples["date"] = "March 4, 2021";
        samples["discord.invite"] = "discord.gg/abc123";
        samples["leaderboard.metric"] = "points";
        samples["leaderboard.list"] =
            "#1 TopFan (900) | #2 SecondPlace (650) | #3 ThirdWheel (400)";
        samples["leaderboard.count"] = "3";
        samples["game.name"] = "coinflip";
        samples["game.bet"] = "50";
        samples["game.payout"] = "100";
        samples["game.balance"] = "1250";
    }
}
