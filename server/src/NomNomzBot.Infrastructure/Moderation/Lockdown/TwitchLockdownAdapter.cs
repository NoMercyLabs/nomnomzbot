// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation.Lockdown;

/// <summary>
/// Twitch's side of lockdown: Shield Mode, the chat-settings gates and AutoMod's overall level.
///
/// <para>Blocked terms are not driven here. Pushing a campaign's skeletons needs a campaign term
/// source this adapter does not have, so a requested <see cref="LockdownControl.BlockedTerms"/> is
/// reported unavailable rather than pretended.</para>
/// </summary>
public sealed class TwitchLockdownAdapter : IPlatformLockdownAdapter
{
    /// <summary>Minimum follow age a lockdown asks for, in minutes.</summary>
    internal const int LockedFollowerMinutes = 10;

    /// <summary>Slow-mode wait a lockdown asks for, in seconds.</summary>
    internal const int LockedSlowSeconds = 30;

    /// <summary>AutoMod's strictest overall level.</summary>
    internal const int LockedAutoModLevel = 4;

    private static readonly IReadOnlySet<LockdownControl> ChatControls =
        new HashSet<LockdownControl>
        {
            LockdownControl.FollowersOnly,
            LockdownControl.SlowMode,
            LockdownControl.UniqueChat,
            LockdownControl.SubscribersOnly,
        };

    private readonly ITwitchChatApi _chat;
    private readonly ITwitchModerationApi _moderation;

    public TwitchLockdownAdapter(ITwitchChatApi chat, ITwitchModerationApi moderation)
    {
        _chat = chat;
        _moderation = moderation;
    }

    public string Platform => "twitch";

    public IReadOnlySet<LockdownControl> Drives { get; } =
        PlatformLockdownCapabilities
            .Twitch.Supported.Where(c => c != LockdownControl.BlockedTerms)
            .ToHashSet();

    public async Task<IReadOnlyDictionary<LockdownControl, Result<ControlReading>>> ReadAsync(
        Guid broadcasterId,
        IReadOnlyCollection<LockdownControl> controls,
        CancellationToken ct
    )
    {
        Dictionary<LockdownControl, Result<ControlReading>> readings = [];

        List<LockdownControl> chatWanted = controls.Where(ChatControls.Contains).ToList();
        if (chatWanted.Count > 0)
        {
            Result<TwitchChatSettings> chat = await _chat.GetChatSettingsAsync(broadcasterId, ct);
            foreach (LockdownControl control in chatWanted)
                readings[control] = chat.IsSuccess
                    ? Result.Success(ReadChatControl(control, chat.Value))
                    : Failed(chat);
        }

        if (controls.Contains(LockdownControl.ShieldMode))
        {
            Result<TwitchShieldModeStatus> shield = await _moderation.GetShieldModeStatusAsync(
                broadcasterId,
                ct
            );
            readings[LockdownControl.ShieldMode] = shield.IsSuccess
                ? Result.Success(
                    new ControlReading(
                        JsonSerializer.Serialize(shield.Value.IsActive),
                        shield.Value.IsActive
                    )
                )
                : Failed(shield);
        }

        if (controls.Contains(LockdownControl.StrictAutoMod))
        {
            Result<TwitchAutoModSettings> autoMod = await _moderation.GetAutoModSettingsAsync(
                broadcasterId,
                ct
            );
            readings[LockdownControl.StrictAutoMod] = autoMod.IsSuccess
                ? Result.Success(
                    new ControlReading(
                        JsonSerializer.Serialize(AutoModSnapshot.From(autoMod.Value)),
                        autoMod.Value.OverallLevel == LockedAutoModLevel
                    )
                )
                : Failed(autoMod);
        }

        return readings;
    }

    public async Task<Result> LockAsync(
        Guid broadcasterId,
        LockdownControl control,
        CancellationToken ct
    )
    {
        switch (control)
        {
            case LockdownControl.ShieldMode:
                return await _moderation.UpdateShieldModeStatusAsync(broadcasterId, true, ct);
            case LockdownControl.StrictAutoMod:
                return await _moderation.UpdateAutoModSettingsAsync(
                    broadcasterId,
                    new UpdateAutoModSettingsRequest(OverallLevel: LockedAutoModLevel),
                    ct
                );
            case LockdownControl.FollowersOnly:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        FollowerMode: true,
                        FollowerModeDuration: LockedFollowerMinutes
                    ),
                    ct
                );
            case LockdownControl.SlowMode:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        SlowMode: true,
                        SlowModeWaitTime: LockedSlowSeconds
                    ),
                    ct
                );
            case LockdownControl.UniqueChat:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(UniqueChatMode: true),
                    ct
                );
            case LockdownControl.SubscribersOnly:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(SubscriberMode: true),
                    ct
                );
            default:
                return Result.Failure($"lockdown_control_not_driven:{control}", "NOT_SUPPORTED");
        }
    }

    public async Task<Result> RestoreAsync(
        Guid broadcasterId,
        EngagedControl engaged,
        CancellationToken ct
    )
    {
        switch (engaged.Control)
        {
            case LockdownControl.ShieldMode:
                return await _moderation.UpdateShieldModeStatusAsync(
                    broadcasterId,
                    JsonSerializer.Deserialize<bool>(engaged.PreviousValue),
                    ct
                );
            case LockdownControl.StrictAutoMod:
                return await _moderation.UpdateAutoModSettingsAsync(
                    broadcasterId,
                    JsonSerializer.Deserialize<AutoModSnapshot>(engaged.PreviousValue)!.ToRequest(),
                    ct
                );
            case LockdownControl.FollowersOnly:
                GateValue follow = JsonSerializer.Deserialize<GateValue>(engaged.PreviousValue)!;
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        FollowerMode: follow.Enabled,
                        FollowerModeDuration: follow.Enabled ? follow.Amount : null
                    ),
                    ct
                );
            case LockdownControl.SlowMode:
                GateValue slow = JsonSerializer.Deserialize<GateValue>(engaged.PreviousValue)!;
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        SlowMode: slow.Enabled,
                        SlowModeWaitTime: slow.Enabled ? slow.Amount : null
                    ),
                    ct
                );
            case LockdownControl.UniqueChat:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        UniqueChatMode: JsonSerializer.Deserialize<bool>(engaged.PreviousValue)
                    ),
                    ct
                );
            case LockdownControl.SubscribersOnly:
                return await _chat.UpdateChatSettingsAsync(
                    broadcasterId,
                    new UpdateChatSettingsRequest(
                        SubscriberMode: JsonSerializer.Deserialize<bool>(engaged.PreviousValue)
                    ),
                    ct
                );
            default:
                return Result.Failure(
                    $"lockdown_control_not_driven:{engaged.Control}",
                    "NOT_SUPPORTED"
                );
        }
    }

    private static ControlReading ReadChatControl(
        LockdownControl control,
        TwitchChatSettings chat
    ) =>
        control switch
        {
            LockdownControl.FollowersOnly => new ControlReading(
                JsonSerializer.Serialize(
                    new GateValue(chat.FollowerMode, chat.FollowerModeDuration ?? 0)
                ),
                chat.FollowerMode && (chat.FollowerModeDuration ?? 0) >= LockedFollowerMinutes
            ),
            LockdownControl.SlowMode => new ControlReading(
                JsonSerializer.Serialize(new GateValue(chat.SlowMode, chat.SlowModeWaitTime)),
                chat.SlowMode && (chat.SlowModeWaitTime ?? 0) >= LockedSlowSeconds
            ),
            LockdownControl.UniqueChat => new ControlReading(
                JsonSerializer.Serialize(chat.UniqueChatMode),
                chat.UniqueChatMode
            ),
            _ => new ControlReading(
                JsonSerializer.Serialize(chat.SubscriberMode),
                chat.SubscriberMode
            ),
        };

    private static Result<ControlReading> Failed(Result failure) =>
        Result.Failure<ControlReading>(failure.ErrorMessage, failure.ErrorCode);

    /// <summary>A gate that is on or off and, when on, carries a number (minutes or seconds).</summary>
    private sealed record GateValue(bool Enabled, int? Amount);

    /// <summary>
    /// AutoMod as it was. Twitch drives AutoMod from one overall level OR nine category levels, and
    /// overall is null in category mode: both are kept so either shape restores exactly.
    /// </summary>
    private sealed record AutoModSnapshot(
        int? OverallLevel,
        int Aggression,
        int Bullying,
        int Disability,
        int Misogyny,
        int RaceEthnicityOrReligion,
        int SexBasedTerms,
        int SexualitySexOrGender,
        int Swearing
    )
    {
        public static AutoModSnapshot From(TwitchAutoModSettings s) =>
            new(
                s.OverallLevel,
                s.Aggression,
                s.Bullying,
                s.Disability,
                s.Misogyny,
                s.RaceEthnicityOrReligion,
                s.SexBasedTerms,
                s.SexualitySexOrGender,
                s.Swearing
            );

        public UpdateAutoModSettingsRequest ToRequest() =>
            OverallLevel is not null
                ? new UpdateAutoModSettingsRequest(OverallLevel: OverallLevel)
                : new UpdateAutoModSettingsRequest(
                    Aggression: Aggression,
                    Bullying: Bullying,
                    Disability: Disability,
                    Misogyny: Misogyny,
                    RaceEthnicityOrReligion: RaceEthnicityOrReligion,
                    SexBasedTerms: SexBasedTerms,
                    SexualitySexOrGender: SexualitySexOrGender,
                    Swearing: Swearing
                );
    }
}
