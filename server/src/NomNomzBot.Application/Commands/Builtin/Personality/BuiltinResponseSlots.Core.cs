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
/// Reply slots of the core chat built-ins (<c>!uptime</c>, <c>!commands</c>, <c>!lurk</c>, <c>!whisper</c>, …)
/// and the bot's own status lines.
/// </summary>
public static partial class BuiltinResponseSlots
{
    /// <summary><c>!uptime</c> — how long the stream has been live.</summary>
    public static class Uptime
    {
        public const string Key = "uptime";

        /// <summary>Stream is live; <c>{uptime}</c> carries the real elapsed time.</summary>
        public const string Live = "live";

        /// <summary>Stream is offline.</summary>
        public const string Offline = "offline";
    }

    /// <summary><c>!commands</c>/<c>!help</c> (generic fallback) — the enabled-trigger listing.</summary>
    public static class Commands
    {
        public const string Key = "commands";

        /// <summary>At least one trigger is enabled; <c>{user}</c>/<c>{commands}</c>/<c>{prefix}</c> are set.</summary>
        public const string List = "list";

        /// <summary>No triggers are enabled in the channel; <c>{user}</c> is set.</summary>
        public const string Empty = "empty";
    }

    /// <summary><c>!help &lt;name&gt;</c> — an authored command's own description.</summary>
    public static class Help
    {
        public const string Key = "help";

        /// <summary>A described command was found; <c>{user}</c>/<c>{command}</c>/<c>{description}</c> are set.</summary>
        public const string Described = "described";

        /// <summary>No argument; <c>{user}</c>/<c>{prefix}</c> are set.</summary>
        public const string Usage = "usage";

        /// <summary>No command with that name; <c>{user}</c>/<c>{command}</c>/<c>{prefix}</c> are set.</summary>
        public const string Unknown = "unknown";

        /// <summary>The command exists but carries no description; <c>{user}</c>/<c>{command}</c>/<c>{prefix}</c> are set.</summary>
        public const string NoDescription = "nodescription";
    }

    /// <summary><c>!lurk</c>/<c>!unlurk</c> — the caller's lurking-flag flip.</summary>
    public static class Lurk
    {
        public const string Key = "lurk";

        /// <summary>The caller is now marked lurking; <c>{user}</c> is set.</summary>
        public const string Lurking = "lurking";

        /// <summary>The caller's lurking flag was cleared; <c>{user}</c> is set.</summary>
        public const string NotLurking = "notlurking";

        /// <summary>The caller's account could not be resolved, so the flag was not changed; <c>{user}</c> is set.</summary>
        public const string AccountUnresolved = "accountunresolved";
    }

    /// <summary><c>!accountage</c> — how long the caller's Twitch account has existed.</summary>
    public static class AccountAge
    {
        public const string Key = "accountage";

        /// <summary>The age was resolved; <c>{user}</c>, <c>{date}</c> (creation date) and <c>{age}</c> are set.</summary>
        public const string Age = "age";

        /// <summary>The caller's account could not be resolved; <c>{user}</c> is set.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>The live Twitch lookup did not answer; <c>{user}</c> is set.</summary>
        public const string TwitchUnavailable = "twitchunavailable";

        /// <summary>Twitch has no account for the caller; <c>{user}</c> is set.</summary>
        public const string NotFound = "notfound";

        /// <summary>The account was found but carries no creation date; <c>{user}</c> is set.</summary>
        public const string Undetermined = "undetermined";
    }

    /// <summary><c>!followage</c> — how long the caller has followed the channel.</summary>
    public static class FollowAge
    {
        public const string Key = "followage";

        /// <summary>The caller follows; <c>{user}</c>/<c>{age}</c> are set.</summary>
        public const string Age = "age";

        /// <summary>The caller does not follow the channel; <c>{user}</c> is set.</summary>
        public const string NotFollowing = "notfollowing";

        /// <summary>The live Twitch follower lookup failed; <c>{user}</c> is set.</summary>
        public const string TwitchUnavailable = "twitchunavailable";
    }

    /// <summary><c>!whisper &lt;user&gt; &lt;message&gt;</c> — usage/error tone slots (S069h).</summary>
    public static class Whisper
    {
        public const string Key = "whisper";

        /// <summary>Too few arguments were given — no variables.</summary>
        public const string Usage = "usage";

        /// <summary>No Twitch user matched the given login; <c>{user}</c> is set.</summary>
        public const string NotFound = "notfound";

        /// <summary>The Helix lookup call itself failed (Twitch did not answer) — no variables.</summary>
        public const string TwitchUnavailable = "twitchunavailable";

        /// <summary>No direct-message sender is bound for the target platform — no variables.</summary>
        public const string NotAvailable = "notavailable";

        /// <summary>The whisper was sent; <c>{user}</c> is the recipient's display name.</summary>
        public const string Sent = "sent";

        /// <summary>The platform refused the whisper; <c>{user}</c> is the recipient's display name.</summary>
        public const string SendFailed = "sendfailed";
    }

    /// <summary><c>!discord</c> — points viewers at the channel's linked Discord server via a live-created invite.</summary>
    public static class Discord
    {
        public const string Key = "discord";

        /// <summary>An invite was created; <c>{invite}</c> is set.</summary>
        public const string Invite = "invite";

        /// <summary>No active Discord guild link for this channel — no variables.</summary>
        public const string NotConnected = "notconnected";

        /// <summary>The link exists but no invitable channel/invite could be created — no variables.</summary>
        public const string Unavailable = "unavailable";
    }

    /// <summary><c>!update</c> — usage/error tone slots (S069h).</summary>
    public static class UpdateUserInfo
    {
        public const string Key = "update";

        /// <summary>No Twitch user matched the given login; <c>{user}</c> is set.</summary>
        public const string NotFound = "notfound";

        /// <summary>The Helix lookup call itself failed (Twitch did not answer); <c>{user}</c> is set.</summary>
        public const string TwitchUnavailable = "twitchunavailable";

        /// <summary>The refresh write itself failed; <c>{user}</c> is set.</summary>
        public const string UpdateFailed = "updatefailed";

        /// <summary>The requested login could not be resolved (empty caller login); <c>{user}</c> is set.</summary>
        public const string LoginUnresolved = "loginunresolved";

        /// <summary>A sub-moderator caller tried to update someone else; <c>{user}</c> is set.</summary>
        public const string OwnInfoOnly = "owninfoonly";

        /// <summary>The profile was refreshed; <c>{user}</c> is the refreshed user's display name.</summary>
        public const string Updated = "updated";
    }

    /// <summary><c>!leaderboard</c> — the channel's points leaderboard.</summary>
    public static class Leaderboard
    {
        public const string Key = "leaderboard";

        /// <summary>The channel has no currency set up.</summary>
        public const string None = "none";

        /// <summary>Nobody has points yet.</summary>
        public const string Empty = "empty";

        /// <summary>The top list.</summary>
        public const string Top = "top";
    }

    /// <summary>
    /// <c>!coinflip</c>/<c>!dice</c>/<c>!slots</c> — usage/error tone slots (S069h). Registered per-game under each
    /// game's own <c>BuiltinKey</c> (the chat trigger word) since the three share this base but are distinct
    /// built-ins.
    /// </summary>
    public static class Game
    {
        /// <summary>The caller's own account could not be resolved/created — no variables.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>No bet (or a bet that is not a positive whole number) was given; <c>{game.name}</c> is set.</summary>
        public const string Usage = "usage";

        /// <summary>The game is not switched on for the channel; <c>{game.name}</c> is set.</summary>
        public const string NotEnabled = "notenabled";

        /// <summary>The channel's game list could not be read — no variables.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The player won; <c>{game.name}</c>, <c>{game.bet}</c>, <c>{game.payout}</c>, <c>{game.balance}</c> are set.</summary>
        public const string Won = "won";

        /// <summary>The player lost; <c>{game.name}</c>, <c>{game.bet}</c>, <c>{game.balance}</c> are set.</summary>
        public const string Lost = "lost";

        /// <summary>The bet is below the minimum or above the maximum — no variables.</summary>
        public const string BetOutOfRange = "betoutofrange";

        /// <summary>The player cannot cover the bet — no variables.</summary>
        public const string InsufficientFunds = "insufficientfunds";

        /// <summary>The player must confirm they are 18 or older first — no variables.</summary>
        public const string AgeConsentRequired = "ageconsentrequired";

        /// <summary>The player's standing is below the game's floor — no variables.</summary>
        public const string NotAllowed = "notallowed";

        /// <summary>The player played too recently — no variables.</summary>
        public const string OnCooldown = "oncooldown";

        /// <summary>The player used all their plays for this stream — no variables.</summary>
        public const string StreamLimit = "streamlimit";

        /// <summary>The channel has no currency switched on — no variables.</summary>
        public const string CurrencyDisabled = "currencydisabled";

        /// <summary>Any other failure while playing — no variables.</summary>
        public const string PlayFailed = "playfailed";
    }

    /// <summary>
    /// The chat handler's own lines about ANY command (not one built-in's reply) — spoken in the channel's
    /// personality and re-wordable like a built-in reply. No command owns this group.
    /// </summary>
    public static class SystemReplies
    {
        public const string Key = "system";

        /// <summary>The caller's role is below the command's floor — no variables.</summary>
        public const string PermissionDenied = "permissiondenied";

        /// <summary>The command is still cooling down — no variables.</summary>
        public const string Cooldown = "cooldown";

        /// <summary>The command started but failed to finish — no variables.</summary>
        public const string Failed = "failed";

        /// <summary>A pipeline command has no saved pipeline to run, so nothing happened — no variables.</summary>
        public const string NothingRan = "nothingran";
    }

    /// <summary>The bot's own status lines — not a chat command, but spoken in the channel's personality like one.</summary>
    public static class BotStatus
    {
        public const string Key = "botstatus";

        /// <summary>The bot is stopping with no successor taking over (restart / crash-restart / manual stop).</summary>
        public const string GoingOffline = "goingoffline";

        /// <summary>The Twitch event connection dropped unplanned — redeems and commands are paused. Once per outage.</summary>
        public const string ConnectionLost = "connectionlost";

        /// <summary>The Twitch event connection is back after an announced outage.</summary>
        public const string ConnectionRestored = "connectionrestored";
    }
}
