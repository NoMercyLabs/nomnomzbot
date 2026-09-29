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
/// Reply slots of the community built-ins (<c>!stats</c>, <c>!quote</c>, <c>!permit</c>, <c>!voice</c>,
/// <c>!media</c>) and the data-rights built-ins.
/// </summary>
public static partial class BuiltinResponseSlots
{
    /// <summary>
    /// <c>!forgetme</c> — self-service GDPR erasure (gdpr-crypto.md §9). Only the friendly completion copy is
    /// customizable; the informed-re-entry clause is appended by the built-in itself and is NOT part of any
    /// template.
    /// </summary>
    public static class Forgetme
    {
        public const string Key = "forgetme";

        /// <summary>Erasure completed — the streamer-stylable "clean slate" sentence (part 1 of the reply).</summary>
        public const string Done = "done";
    }

    /// <summary><c>!stats</c> / <c>!profile</c> — a viewer's headline stats line.</summary>
    public static class Stats
    {
        public const string Key = "stats";

        /// <summary>The composed profile line; <c>{stats.*}</c> variables are set.</summary>
        public const string Profile = "profile";

        /// <summary>The viewer (or the @mentioned name) has no recorded activity in this channel.</summary>
        public const string NotSeen = "notseen";

        /// <summary>The caller's own account could not be resolved.</summary>
        public const string AccountUnresolved = "accountunresolved";
    }

    /// <summary><c>!quote</c> — the quote library in chat (quotes.md section 4).</summary>
    public static class Quote
    {
        public const string Key = "quote";

        /// <summary>A quote read back (random or by number).</summary>
        public const string Show = "show";

        /// <summary>A random read on a channel with no quotes.</summary>
        public const string Empty = "empty";

        /// <summary>A numbered quote that does not exist (read, edit or delete).</summary>
        public const string NotFound = "notfound";

        /// <summary>A quote was added.</summary>
        public const string Added = "added";

        /// <summary><c>!quote add</c> with no text and no reply target.</summary>
        public const string AddUsage = "addusage";

        /// <summary>The add failed for a reason other than invalid text.</summary>
        public const string AddFailed = "addfailed";

        /// <summary>The quote text was empty or too long.</summary>
        public const string InvalidText = "invalidtext";

        /// <summary>A quote was re-worded.</summary>
        public const string Updated = "updated";

        /// <summary><c>!quote edit</c> without a number and new text.</summary>
        public const string EditUsage = "editusage";

        /// <summary>The edit failed for a reason other than invalid text.</summary>
        public const string EditFailed = "editfailed";

        /// <summary>A quote was deleted.</summary>
        public const string Deleted = "deleted";

        /// <summary><c>!quote del</c> without a number.</summary>
        public const string DeleteUsage = "deleteusage";

        /// <summary>The caller's account could not be resolved.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>The caller may not add quotes.</summary>
        public const string NoAddPermission = "noaddpermission";

        /// <summary>The caller may not edit quotes.</summary>
        public const string NoEditPermission = "noeditpermission";

        /// <summary>The caller may not delete quotes.</summary>
        public const string NoDeletePermission = "nodeletepermission";
    }

    /// <summary><c>!voice</c> — the viewer self-service TTS voice picker (tts.md section 6.1).</summary>
    public static class Voice
    {
        public const string Key = "voice";

        /// <summary>The viewer has their own voice.</summary>
        public const string Current = "current";

        /// <summary>The viewer uses the channel default voice.</summary>
        public const string CurrentDefault = "currentdefault";

        /// <summary>The viewer's voice was reset to the channel default.</summary>
        public const string Cleared = "cleared";

        /// <summary>Resetting the voice failed.</summary>
        public const string ClearFailed = "clearfailed";

        /// <summary>A voice was set and it was the only match.</summary>
        public const string Set = "set";

        /// <summary>A voice was set and other voices matched too.</summary>
        public const string SetMany = "setmany";

        /// <summary>Setting the voice failed for a reason other than a lock.</summary>
        public const string SetFailed = "setfailed";

        /// <summary>No voice matched the search.</summary>
        public const string NoMatch = "nomatch";

        /// <summary>TTS or viewer voice picking is turned off on this channel.</summary>
        public const string Disabled = "disabled";

        /// <summary>Roulette picked a random voice and kept it.</summary>
        public const string Roulette = "roulette";

        /// <summary>The languages the catalogue can speak.</summary>
        public const string Languages = "languages";

        /// <summary>The voices for one language.</summary>
        public const string Voices = "voices";

        /// <summary>The voices for one language, with more not shown.</summary>
        public const string VoicesMore = "voicesmore";

        /// <summary>No voices exist for the asked language.</summary>
        public const string NoVoicesForLanguage = "novoicesforlanguage";

        /// <summary>The voice catalogue is empty or unreadable.</summary>
        public const string NoVoices = "novoices";

        /// <summary><c>!voice get</c> without a language.</summary>
        public const string GetUsage = "getusage";

        /// <summary><c>!voice set</c> without a name.</summary>
        public const string SetUsage = "setusage";
    }

    /// <summary><c>!media</c> — submit a clip or video to the media queue (media-share.md section 4).</summary>
    public static class Media
    {
        public const string Key = "media";

        /// <summary><c>!media</c> without a link.</summary>
        public const string Usage = "usage";

        /// <summary>The caller's account could not be resolved.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>The submission was approved and queued.</summary>
        public const string Queued = "queued";

        /// <summary>The submission waits for a moderator.</summary>
        public const string Pending = "pending";

        /// <summary>Media share is turned off on this channel.</summary>
        public const string Disabled = "disabled";

        /// <summary>The link is not an accepted clip or video.</summary>
        public const string SourceNotAllowed = "sourcenotallowed";

        /// <summary>The clip or video does not exist.</summary>
        public const string NotFound = "notfound";

        /// <summary>The clip or video is longer than the channel allows.</summary>
        public const string TooLong = "toolong";

        /// <summary>The caller may not submit (for example subscriber only).</summary>
        public const string NotEligible = "noteligible";

        /// <summary>The caller submitted too recently.</summary>
        public const string Cooldown = "cooldown";

        /// <summary>The media queue is full.</summary>
        public const string QueueFull = "queuefull";

        /// <summary>A lookup service (Twitch or YouTube) could not answer.</summary>
        public const string Unavailable = "unavailable";

        /// <summary>The submission failed for another reason (for example not enough points).</summary>
        public const string Failed = "failed";
    }

    /// <summary><c>!permit</c> — grant a temporary role or capability.</summary>
    public static class Permit
    {
        public const string Key = "permit";

        /// <summary>The command was used without the needed arguments.</summary>
        public const string Usage = "usage";

        /// <summary>The caller may not manage permits.</summary>
        public const string NotAllowed = "notallowed";

        /// <summary>The caller's account could not be resolved.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>No user was mentioned.</summary>
        public const string NoTarget = "notarget";

        /// <summary>The mentioned user was not found on Twitch.</summary>
        public const string TargetNotFound = "targetnotfound";

        /// <summary>The mentioned user could not be resolved.</summary>
        public const string TargetUnresolved = "targetunresolved";

        /// <summary>A role was granted.</summary>
        public const string GrantedRole = "grantedrole";

        /// <summary>A capability was granted.</summary>
        public const string GrantedCapability = "grantedcapability";

        /// <summary>The role is above the caller's own level.</summary>
        public const string RoleTooHigh = "roletoohigh";

        /// <summary>The capability does not exist, cannot be delegated, or the caller does not hold it.</summary>
        public const string CapabilityDenied = "capabilitydenied";

        /// <summary>The grant failed for another reason.</summary>
        public const string GrantFailed = "grantfailed";
    }

    /// <summary><c>!unpermit</c> — revoke temporary grants.</summary>
    public static class Unpermit
    {
        public const string Key = "unpermit";

        /// <summary>The command was used without the needed arguments.</summary>
        public const string Usage = "usage";

        /// <summary>The caller may not manage permits.</summary>
        public const string NotAllowed = "notallowed";

        /// <summary>The caller's account could not be resolved.</summary>
        public const string AccountUnresolved = "accountunresolved";

        /// <summary>No user was mentioned.</summary>
        public const string NoTarget = "notarget";

        /// <summary>The mentioned user was not found on Twitch.</summary>
        public const string TargetNotFound = "targetnotfound";

        /// <summary>The mentioned user could not be resolved.</summary>
        public const string TargetUnresolved = "targetunresolved";

        /// <summary>All of the user's grants were revoked.</summary>
        public const string RevokedAll = "revokedall";

        /// <summary>One named grant was revoked.</summary>
        public const string Revoked = "revoked";

        /// <summary>The revoke failed.</summary>
        public const string RevokeFailed = "revokefailed";
    }
}
