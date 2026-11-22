// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The reply catalogue (commands-pipelines.md §11) is what a streamer edits, so every shipped line must pass the
/// SAME save-time check a streamer's own text does: every placeholder is a declared slot variable or a registered
/// template helper. A shipped line that names an undeclared variable would render a raw "{x}" in chat — and would
/// be un-saveable if the streamer copied it into the editor.
/// </summary>
public sealed class ToneTemplateCatalogIntegrityTests
{
    private static readonly string[] Tones =
    [
        PersonalityTone.Informative,
        PersonalityTone.Friendly,
        PersonalityTone.Sassy,
        PersonalityTone.Hype,
        PersonalityTone.Chill,
    ];

    [Fact]
    public void The_two_uptime_slots_ship_dutch_text()
    {
        ToneTemplateCatalog
            .TranslatedSlots("nl")
            .Should()
            .Contain([
                (BuiltinResponseSlots.Uptime.Key, BuiltinResponseSlots.Uptime.Live),
                (BuiltinResponseSlots.Uptime.Key, BuiltinResponseSlots.Uptime.Offline),
            ]);
    }

    [Fact]
    public void Every_slot_the_music_catalogue_file_adds_ships_dutch_text()
    {
        string[] musicKeys =
        [
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Pause.Key,
            BuiltinResponseSlots.Resume.Key,
            BuiltinResponseSlots.Playlist.Key,
        ];

        List<(string BuiltinKey, string Slot)> musicSlots =
        [
            .. ToneTemplateCatalog.AllSlots().Where(s => musicKeys.Contains(s.BuiltinKey)),
        ];
        IReadOnlyList<(string BuiltinKey, string Slot)> translated =
            ToneTemplateCatalog.TranslatedSlots("nl");

        musicSlots.Should().HaveCountGreaterThan(70);
        List<string> missing =
        [
            .. musicSlots
                .Where(s => !translated.Contains(s))
                .Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];
        missing
            .Should()
            .BeEmpty("every music slot needs Dutch text; missing: {0}", string.Join(", ", missing));
    }

    [Fact]
    public void Every_slot_the_community_catalogue_file_adds_ships_dutch_text()
    {
        // The community file adds these builtins; the whitelist and mass-ban slots live in their own files.
        string[] communityKeys =
        [
            BuiltinResponseSlots.Forgetme.Key,
            BuiltinResponseSlots.Shoutout.Key,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Quote.Key,
            BuiltinResponseSlots.Voice.Key,
            BuiltinResponseSlots.Media.Key,
            BuiltinResponseSlots.Permit.Key,
            BuiltinResponseSlots.Unpermit.Key,
        ];

        List<(string BuiltinKey, string Slot)> communitySlots =
        [
            .. ToneTemplateCatalog.AllSlots().Where(s => communityKeys.Contains(s.BuiltinKey)),
        ];
        IReadOnlyList<(string BuiltinKey, string Slot)> translated =
            ToneTemplateCatalog.TranslatedSlots("nl");

        communitySlots.Should().HaveCountGreaterThan(70);
        List<string> missing =
        [
            .. communitySlots
                .Where(s => !translated.Contains(s))
                .Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];
        missing
            .Should()
            .BeEmpty(
                "every community slot needs Dutch text; missing: {0}",
                string.Join(", ", missing)
            );
    }

    [Fact]
    public void Every_slot_the_base_catalogue_file_adds_ships_dutch_text()
    {
        // The slots that ToneTemplateCatalog.cs itself adds (the two uptime slots have their own test above).
        (string, string)[] baseSlots =
        [
            (BuiltinResponseSlots.Uptime.Key, BuiltinResponseSlots.Uptime.Live),
            (BuiltinResponseSlots.Uptime.Key, BuiltinResponseSlots.Uptime.Offline),
            (BuiltinResponseSlots.Song.Key, BuiltinResponseSlots.Song.Playing),
            (BuiltinResponseSlots.Song.Key, BuiltinResponseSlots.Song.Nothing),
            (BuiltinResponseSlots.Queue.Key, BuiltinResponseSlots.Queue.List),
            (BuiltinResponseSlots.Queue.Key, BuiltinResponseSlots.Queue.Empty),
            (BuiltinResponseSlots.Queue.Key, BuiltinResponseSlots.Queue.Mine),
            (BuiltinResponseSlots.Queue.Key, BuiltinResponseSlots.Queue.None),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequest.Added),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequest.Duplicate),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequest.AlreadyPlaying),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequest.NotFound),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequest.NoCloseMatch),
            (BuiltinResponseSlots.SongRequest.Key, BuiltinResponseSlots.SongRequestErrors.Disabled),
            (BuiltinResponseSlots.Skip.Key, BuiltinResponseSlots.Skip.Skipped),
            (BuiltinResponseSlots.Commands.Key, BuiltinResponseSlots.Commands.List),
            (BuiltinResponseSlots.Commands.Key, BuiltinResponseSlots.Commands.Empty),
            (BuiltinResponseSlots.Help.Key, BuiltinResponseSlots.Help.Described),
            (BuiltinResponseSlots.Help.Key, BuiltinResponseSlots.Help.Usage),
            (BuiltinResponseSlots.Help.Key, BuiltinResponseSlots.Help.Unknown),
            (BuiltinResponseSlots.Help.Key, BuiltinResponseSlots.Help.NoDescription),
            (BuiltinResponseSlots.Lurk.Key, BuiltinResponseSlots.Lurk.Lurking),
            (BuiltinResponseSlots.Lurk.Key, BuiltinResponseSlots.Lurk.NotLurking),
            (BuiltinResponseSlots.AccountAge.Key, BuiltinResponseSlots.AccountAge.Age),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.Usage),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.NotFound),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.TwitchUnavailable),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.NotAvailable),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.CapReached),
            (BuiltinResponseSlots.BanSong.Key, BuiltinResponseSlots.BanSong.Nothing),
            (BuiltinResponseSlots.BanSong.Key, BuiltinResponseSlots.BanSong.CouldNotBan),
            (BuiltinResponseSlots.UpdateUserInfo.Key, BuiltinResponseSlots.UpdateUserInfo.NotFound),
            (
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.TwitchUnavailable
            ),
            (
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.UpdateFailed
            ),
            (
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.LoginUnresolved
            ),
            (
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.OwnInfoOnly
            ),
            (BuiltinResponseSlots.Volume.Key, BuiltinResponseSlots.Volume.Usage),
            (BuiltinResponseSlots.Volume.Key, BuiltinResponseSlots.Volume.CannotRead),
            ("coinflip", BuiltinResponseSlots.Game.AccountUnresolved),
            ("dice", BuiltinResponseSlots.Game.AccountUnresolved),
            ("slots", BuiltinResponseSlots.Game.AccountUnresolved),
            (BuiltinResponseSlots.BotStatus.Key, BuiltinResponseSlots.BotStatus.GoingOffline),
            (BuiltinResponseSlots.BotStatus.Key, BuiltinResponseSlots.BotStatus.ConnectionLost),
            (BuiltinResponseSlots.BotStatus.Key, BuiltinResponseSlots.BotStatus.ConnectionRestored),
        ];
        IReadOnlyList<(string BuiltinKey, string Slot)> translated =
            ToneTemplateCatalog.TranslatedSlots("nl");

        baseSlots.Should().HaveCount(44);
        baseSlots.Should().OnlyContain(s => ToneTemplateCatalog.Contains(s.Item1, s.Item2));
        List<string> missing =
        [
            .. baseSlots.Where(s => !translated.Contains(s)).Select(s => $"{s.Item1}/{s.Item2}"),
        ];
        missing
            .Should()
            .BeEmpty("every base slot needs Dutch text; missing: {0}", string.Join(", ", missing));
    }

    [Fact]
    public void Every_slot_the_core_catalogue_file_adds_ships_dutch_text()
    {
        // Slots of keys that the core file shares with other catalogue files are listed one by one.
        (string, string)[] coreSlots =
        [
            (BuiltinResponseSlots.Lurk.Key, BuiltinResponseSlots.Lurk.AccountUnresolved),
            (
                BuiltinResponseSlots.AccountAge.Key,
                BuiltinResponseSlots.AccountAge.AccountUnresolved
            ),
            (
                BuiltinResponseSlots.AccountAge.Key,
                BuiltinResponseSlots.AccountAge.TwitchUnavailable
            ),
            (BuiltinResponseSlots.AccountAge.Key, BuiltinResponseSlots.AccountAge.NotFound),
            (BuiltinResponseSlots.AccountAge.Key, BuiltinResponseSlots.AccountAge.Undetermined),
            (BuiltinResponseSlots.AccountAge.Key, BuiltinResponseSlots.AccountAge.AgeOther),
            (BuiltinResponseSlots.AccountAge.Key, BuiltinResponseSlots.AccountAge.UserNotFound),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.Age),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.NotFollowing),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.TwitchUnavailable),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.AgeOther),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.NotFollowingOther),
            (BuiltinResponseSlots.FollowAge.Key, BuiltinResponseSlots.FollowAge.UserNotFound),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.Sent),
            (BuiltinResponseSlots.Whisper.Key, BuiltinResponseSlots.Whisper.SendFailed),
            (BuiltinResponseSlots.UpdateUserInfo.Key, BuiltinResponseSlots.UpdateUserInfo.Updated),
            (BuiltinResponseSlots.Discord.Key, BuiltinResponseSlots.Discord.Invite),
            (BuiltinResponseSlots.Discord.Key, BuiltinResponseSlots.Discord.NotConnected),
            (BuiltinResponseSlots.Discord.Key, BuiltinResponseSlots.Discord.Unavailable),
            (BuiltinResponseSlots.Leaderboard.Key, BuiltinResponseSlots.Leaderboard.None),
            (BuiltinResponseSlots.Leaderboard.Key, BuiltinResponseSlots.Leaderboard.Empty),
            (BuiltinResponseSlots.Leaderboard.Key, BuiltinResponseSlots.Leaderboard.Top),
            (BuiltinResponseSlots.Reward.Key, BuiltinResponseSlots.Reward.NoPermission),
            (
                BuiltinResponseSlots.SystemReplies.Key,
                BuiltinResponseSlots.SystemReplies.PermissionDenied
            ),
            (BuiltinResponseSlots.SystemReplies.Key, BuiltinResponseSlots.SystemReplies.Cooldown),
            (BuiltinResponseSlots.SystemReplies.Key, BuiltinResponseSlots.SystemReplies.Failed),
            (BuiltinResponseSlots.SystemReplies.Key, BuiltinResponseSlots.SystemReplies.NothingRan),
        ];
        string[] gameKeys = ["coinflip", "dice", "slots"];
        string[] gameSlots =
        [
            BuiltinResponseSlots.Game.Usage,
            BuiltinResponseSlots.Game.NotEnabled,
            BuiltinResponseSlots.Game.Unavailable,
            BuiltinResponseSlots.Game.Won,
            BuiltinResponseSlots.Game.Lost,
            BuiltinResponseSlots.Game.BetOutOfRange,
            BuiltinResponseSlots.Game.InsufficientFunds,
            BuiltinResponseSlots.Game.AgeConsentRequired,
            BuiltinResponseSlots.Game.NotAllowed,
            BuiltinResponseSlots.Game.OnCooldown,
            BuiltinResponseSlots.Game.StreamLimit,
            BuiltinResponseSlots.Game.CurrencyDisabled,
            BuiltinResponseSlots.Game.PlayFailed,
        ];

        List<(string BuiltinKey, string Slot)> expected =
        [
            .. coreSlots,
            .. gameKeys.SelectMany(key => gameSlots.Select(slot => (key, slot))),
        ];
        IReadOnlyList<(string BuiltinKey, string Slot)> translated =
            ToneTemplateCatalog.TranslatedSlots("nl");

        expected.Should().HaveCount(27 + (3 * 13));
        List<string> missing =
        [
            .. expected.Where(s => !translated.Contains(s)).Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];
        missing
            .Should()
            .BeEmpty("every core slot needs Dutch text; missing: {0}", string.Join(", ", missing));
    }

    [Fact]
    public void Every_slot_of_the_catalogue_ships_dutch_text()
    {
        IReadOnlyList<(string BuiltinKey, string Slot)> translated =
            ToneTemplateCatalog.TranslatedSlots("nl");
        List<string> missing =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .Where(s => !translated.Contains(s))
                .Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];

        missing
            .Should()
            .BeEmpty("every slot needs Dutch text; missing: {0}", string.Join(", ", missing));
    }

    [Fact]
    public void Every_dutch_entry_fills_all_five_tones_with_the_english_shape_and_declared_variables()
    {
        TemplateHelperValidator validator = new();
        List<string> failures = [];

        foreach ((string key, string slot) in ToneTemplateCatalog.TranslatedSlots("nl"))
        {
            IReadOnlyList<string> declared = ToneTemplateCatalog.Variables(key, slot);
            foreach (string tone in Tones)
            {
                IReadOnlyList<string> dutch = ToneTemplateCatalog.Get(tone, key, slot, "nl");
                IReadOnlyList<string> english = ToneTemplateCatalog.Get(tone, key, slot);
                if (dutch.Count != english.Count)
                    failures.Add(
                        $"{key}/{slot} ({tone}): {dutch.Count} Dutch lines, {english.Count} English"
                    );
                if (dutch.SequenceEqual(english))
                    failures.Add($"{key}/{slot} ({tone}): the Dutch text is the English text");

                for (int i = 0; i < dutch.Count; i++)
                {
                    string line = dutch[i];
                    Result valid = validator.Validate(
                        line,
                        TemplateHelperContext.Command,
                        declared
                    );
                    if (valid.IsFailure)
                        failures.Add($"{key}/{slot} ({tone}): {valid.ErrorMessage}");
                    // A variable, a command name and an argument placeholder keep their own case, as in English.
                    string words = Regex.Replace(
                        line,
                        @"\{prefix\}\w+|\{[^}]*\}|<[^>]*>|!\w+",
                        string.Empty
                    );
                    // The case rule applies to a Dutch line only when the English line of the same position keeps it.
                    string englishWords =
                        i < english.Count
                            ? Regex.Replace(
                                english[i],
                                @"\{prefix\}\w+|\{[^}]*\}|<[^>]*>|!\w+",
                                string.Empty
                            )
                            : words;
                    if (
                        tone == PersonalityTone.Hype
                        && words.Any(char.IsLower)
                        && !englishWords.Any(char.IsLower)
                    )
                        failures.Add($"{key}/{slot} (hype) is not in capitals: {line}");
                    if (
                        tone == PersonalityTone.Chill
                        && words != words.ToLowerInvariant()
                        && englishWords == englishWords.ToLowerInvariant()
                    )
                        failures.Add($"{key}/{slot} (chill) is not lower case: {line}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Translating_a_slot_the_english_catalogue_does_not_have_is_not_possible()
    {
        ToneTemplateCatalog
            .Get(PersonalityTone.Sassy, "no-such-builtin", "no-such-slot", "nl")
            .Should()
            .BeEmpty();
        ToneTemplateCatalog
            .TranslatedSlots("nl")
            .Should()
            .OnlyContain(s => ToneTemplateCatalog.Contains(s.BuiltinKey, s.Slot));
    }

    [Fact]
    public void Every_shipped_line_uses_only_its_slots_declared_variables_or_registered_helpers()
    {
        TemplateHelperValidator validator = new();
        List<string> failures = [];

        foreach ((string key, string slot) in ToneTemplateCatalog.AllSlots())
        {
            IReadOnlyList<string> declared = ToneTemplateCatalog.Variables(key, slot);
            foreach (string tone in Tones)
            foreach (string line in ToneTemplateCatalog.Get(tone, key, slot))
            {
                Result valid = validator.Validate(line, TemplateHelperContext.Command, declared);
                if (valid.IsFailure)
                    failures.Add($"{key}/{slot} ({tone}): {valid.ErrorMessage}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_sassy_shoutout_announcement_carries_the_old_bots_nine_lines_with_the_target_name()
    {
        IReadOnlyList<string> lines = ToneTemplateCatalog.Get(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.Shoutout.Key,
            BuiltinResponseSlots.Shoutout.Announcement
        );

        lines.Should().HaveCount(9);
        lines.Should().OnlyContain(l => l.Contains("{target.displayname}"));
        foreach (string legacy in new[] { "{game}", "{displayname}", "{tense}", "{presentTense}" })
            lines.Should().NotContain(l => l.Contains(legacy));
    }

    [Fact]
    public void Every_tones_shoutout_announcement_has_its_own_pool_that_follows_the_shoutout_rules()
    {
        Regex link = new(@"\{target\.link\}|twitch\.tv|https?://", RegexOptions.IgnoreCase);
        Regex followObject = new(@"\bfollow \{object\}", RegexOptions.IgnoreCase);
        Regex capitalMidSentence = new(@"[a-z,]\s+\{(Subject|Object|Possessive|PresentTense)\}");
        Regex bareVerbAfterSubject = new(@"\{subject\}\s+(?!\{)([a-z']+)", RegexOptions.IgnoreCase);
        string[] safeAfterSubject = ["can", "will", "might", "never", "just", "really", "still"];
        List<string> failures = [];

        foreach (string tone in Tones)
        {
            IReadOnlyList<string> lines = ToneTemplateCatalog.Get(
                tone,
                BuiltinResponseSlots.Shoutout.Key,
                BuiltinResponseSlots.Shoutout.Announcement
            );
            if (lines.Count < 3)
                failures.Add($"{tone}: only {lines.Count} line(s)");
            foreach (string line in lines)
            {
                if (!line.Contains("{target.displayname}"))
                    failures.Add($"{tone}: no name: {line}");
                if (!line.Contains("follow", StringComparison.OrdinalIgnoreCase))
                    failures.Add($"{tone}: no follow call: {line}");
                if (line.Length >= 500)
                    failures.Add($"{tone}: {line.Length} characters: {line}");
                if (link.IsMatch(line))
                    failures.Add($"{tone}: link: {line}");
                if (followObject.IsMatch(line))
                    failures.Add($"{tone}: follow {{object}}: {line}");
                if (capitalMidSentence.IsMatch(line))
                    failures.Add($"{tone}: capital pronoun mid-sentence: {line}");
                foreach (Match verb in bareVerbAfterSubject.Matches(line))
                    if (!safeAfterSubject.Contains(verb.Groups[1].Value.ToLowerInvariant()))
                        failures.Add($"{tone}: untemplated verb '{verb.Groups[1].Value}': {line}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_sassy_duplicate_song_pool_carries_the_old_bots_ten_lines_with_the_user_name()
    {
        IReadOnlyList<string> lines = ToneTemplateCatalog.Get(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Duplicate
        );

        lines
            .Should()
            .Contain(
                "Congratulations @{user}! That song is already in the queue. Maybe check next time?"
            )
            .And.Contain("Achievement unlocked @{user}: 'Didn't Check the Queue!'")
            .And.Contain(
                "Fun fact @{user}: That song is already queued! Less fun fact: You didn't check first."
            );
        lines.Count(l => l.Contains("@{user}")).Should().Be(10);
        lines.Should().NotContain(l => l.Contains("{name}"));
    }

    [Fact]
    public void A_too_long_track_refusal_has_a_line_in_every_tone_and_the_refunded_one_says_so()
    {
        foreach (string tone in Tones)
        {
            ToneTemplateCatalog
                .Get(
                    tone,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.TrackTooLong
                )
                .Should()
                .NotBeEmpty($"{tone} needs its own too-long line");
            ToneTemplateCatalog
                .Get(
                    tone,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.TrackTooLongRefunded
                )
                .Should()
                .NotBeEmpty($"{tone} needs its own refunded too-long line");
        }

        ToneTemplateCatalog
            .Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.SongRequest.Key,
                BuiltinResponseSlots.SongRequest.TrackTooLongRefunded
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("{track.max}")
            .And.EndWith(", your point has been refunded.");
    }

    [Fact]
    public void Every_slot_ships_a_default_line_the_editor_can_show_and_reset_to()
    {
        List<string> missing =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .Where(s =>
                    string.IsNullOrWhiteSpace(
                        ToneTemplateCatalog.ShippedTemplate(s.BuiltinKey, s.Slot)
                    )
                )
                .Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];

        missing.Should().BeEmpty("a reset must land on real text, never on an empty reply");
    }

    [Fact]
    public void Every_declared_variable_has_a_preview_sample()
    {
        List<string> missing =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .SelectMany(s => ToneTemplateCatalog.Variables(s.BuiltinKey, s.Slot))
                .Distinct()
                .Where(name => !HasSample(name)),
        ];

        missing.Should().BeEmpty("the editor's live preview fills every variable with its sample");
    }

    // queue.more is legitimately empty (nothing more waiting) — its sample is the empty string by design.
    private static bool HasSample(string name) =>
        name == "queue.more" || ToneTemplateCatalog.SampleValue(name).Length > 0;
}
