// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The <c>sound_clip</c> template payload — the portable settings of a <see cref="SoundClip"/> plus the id of
/// the platform audio file whose bytes install copies into the channel. The name is the <c>play_sound</c> slug.
/// </summary>
public sealed record SoundClipTemplatePayload
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public int DefaultVolume { get; init; } = 80;
    public int CooldownSeconds { get; init; }

    /// <summary>The community rung NAME a chatter needs to fire <see cref="TriggerWord"/>, e.g. <c>Subscriber</c>.</summary>
    public string MinPermissionLevel { get; init; } = "Everyone";

    public string? TriggerWord { get; init; }

    /// <summary>
    /// The settings an installed clip holds now. A channel cannot swap a clip's audio, so the audio file is left
    /// out: <see cref="AssetId"/> stays empty and <see cref="ComputeHash"/> never covers it.
    /// </summary>
    public static SoundClipTemplatePayload FromEntity(SoundClip row) =>
        new()
        {
            Name = row.Name,
            DisplayName = row.DisplayName,
            DefaultVolume = row.DefaultVolume,
            CooldownSeconds = row.CooldownSeconds,
            MinPermissionLevel = PermissionLevelNames.ToName(row.MinPermissionLevel),
            TriggerWord = row.TriggerWord,
        };

    public string ComputeHash() => PlatformTemplateJson.Hash(this with { AssetId = Guid.Empty });

    /// <summary>The audio file a stored payload names, or null when the JSON is not a readable payload.</summary>
    public static Guid? ReadAssetId(string payloadJson)
    {
        Result<SoundClipTemplatePayload> parsed =
            PlatformTemplateJson.Parse<SoundClipTemplatePayload>(payloadJson);
        return parsed.IsSuccess && parsed.Value.AssetId != Guid.Empty ? parsed.Value.AssetId : null;
    }
}
