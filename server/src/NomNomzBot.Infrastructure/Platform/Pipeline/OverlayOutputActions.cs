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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Stage;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// The one table of pipeline actions whose only output shows or plays on a browser source, and which required
/// surface (gallery key) each one needs. The live-run warnings and the save-time missing-source check both read
/// it, so a new overlay action is listed once.
/// </summary>
internal static class OverlayOutputActions
{
    private const string TtsSurface = "tts_caption";

    /// <summary>
    /// Action type to the required surface it needs. A null surface means the step targets a widget the user
    /// picked, so no one required surface is implied.
    /// </summary>
    private static readonly Dictionary<string, string?> SurfaceByActionType = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["widget_event"] = null,
        ["play_sound"] = WidgetKeyAliases.Audio,
        ["stop_sound"] = WidgetKeyAliases.Audio,
        ["play_tts"] = TtsSurface,
        ["tts_synthesize"] = TtsSurface,
        ["media_playback"] = WidgetKeyAliases.MediaShare,
        ["stage_effect"] = StageSurface.WidgetKey,
    };

    /// <summary>True when the action type only shows or plays on a browser source.</summary>
    public static bool IsOverlayOutput(string actionType) =>
        SurfaceByActionType.ContainsKey(actionType);

    /// <summary>The required surface the action type needs, or null when it names none.</summary>
    public static string? SurfaceFor(string actionType) =>
        SurfaceByActionType.GetValueOrDefault(actionType);

    /// <summary>
    /// The required surfaces the given action types need that have no open browser source for the channel, each
    /// with the distinct action types that need it. Action types without a surface are left out.
    /// </summary>
    public static List<MissingSourceDto> MissingFor(
        IEnumerable<string> actionTypes,
        IOverlayPresenceRegistry presence,
        Guid broadcasterId
    ) =>
        [
            .. actionTypes
                .Select(type => (Type: type, Surface: SurfaceFor(type)))
                .Where(pair => pair.Surface is not null)
                .GroupBy(pair => pair.Surface!)
                .Where(group => !IsSurfaceConnected(presence, broadcasterId, group.Key))
                .Select(group => new MissingSourceDto(
                    group.Key,
                    [.. group.Select(pair => pair.Type).Distinct(StringComparer.OrdinalIgnoreCase)]
                )),
        ];

    /// <summary>True when a browser source for <paramref name="surfaceKey"/> is open for the channel now.</summary>
    public static bool IsSurfaceConnected(
        IOverlayPresenceRegistry presence,
        Guid broadcasterId,
        string surfaceKey
    ) =>
        surfaceKey switch
        {
            TtsSurface => presence.IsTtsSourceConnected(broadcasterId),
            WidgetKeyAliases.Audio => presence.IsAudioSourceConnected(broadcasterId),
            _ => presence.IsOverlayConnected(broadcasterId),
        };
}
