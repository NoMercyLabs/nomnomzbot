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
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Obs.EventHandlers;

/// <summary>
/// Turns the raw OBS-WS events that change what the dashboard OBS page shows into one typed
/// <see cref="ObsDashboardStateChangedEvent"/> carrying the new value. Every other OBS event is ignored here;
/// it still reaches the trigger source and the automation forwarder, which run off the same bus event.
/// </summary>
public sealed class ObsDashboardStateForwarder : IEventHandler<ObsEventReceivedEvent>
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<ObsDashboardStateForwarder> _logger;

    public ObsDashboardStateForwarder(
        IEventBus eventBus,
        ILogger<ObsDashboardStateForwarder> logger
    )
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task HandleAsync(
        ObsEventReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.ObsEventType))
            return;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(@event.DataJson) ? "{}" : @event.DataJson
            );
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(
                ex,
                "obs.{EventType} carried unparsable data for {Channel}; dashboard forward skipped.",
                @event.ObsEventType,
                @event.BroadcasterId
            );
            return;
        }

        ObsDashboardStateChangedEvent? change;
        using (doc)
            change = Project(@event.BroadcasterId, @event.ObsEventType, doc.RootElement);

        if (change is not null)
            await _eventBus.PublishAsync(change, cancellationToken);
    }

    private static ObsDashboardStateChangedEvent? Project(
        Guid broadcasterId,
        string obsEventType,
        JsonElement data
    ) =>
        obsEventType switch
        {
            "CurrentProgramSceneChanged" => Scene(broadcasterId, ObsStateKinds.ProgramScene, data),
            "CurrentPreviewSceneChanged" => Scene(broadcasterId, ObsStateKinds.PreviewScene, data),
            "StreamStateChanged" => Output(broadcasterId, ObsStateKinds.Stream, data),
            "RecordStateChanged" => Output(broadcasterId, ObsStateKinds.Record, data),
            "ReplayBufferStateChanged" => Output(broadcasterId, ObsStateKinds.ReplayBuffer, data),
            "VirtualcamStateChanged" => Output(broadcasterId, ObsStateKinds.VirtualCam, data),
            "StudioModeStateChanged" => new()
            {
                BroadcasterId = broadcasterId,
                Kind = ObsStateKinds.StudioMode,
                Active = GetBool(data, "studioModeEnabled"),
            },
            "InputMuteStateChanged" => Input(broadcasterId, ObsStateKinds.InputMute, data),
            "InputVolumeChanged" => Input(broadcasterId, ObsStateKinds.InputVolume, data),
            "SceneListChanged" => SceneList(broadcasterId, data),
            "CurrentSceneCollectionChanged" => Named(
                broadcasterId,
                ObsStateKinds.SceneCollection,
                GetString(data, "sceneCollectionName")
            ),
            "CurrentProfileChanged" => Named(
                broadcasterId,
                ObsStateKinds.Profile,
                GetString(data, "profileName")
            ),
            "SceneCollectionListChanged" => NameList(
                broadcasterId,
                ObsStateKinds.SceneCollectionList,
                data,
                "sceneCollections"
            ),
            "ProfileListChanged" => NameList(
                broadcasterId,
                ObsStateKinds.ProfileList,
                data,
                "profiles"
            ),
            "SceneItemEnableStateChanged" => SceneItem(broadcasterId, data),
            _ => null,
        };

    private static ObsDashboardStateChangedEvent? Scene(
        Guid broadcasterId,
        string kind,
        JsonElement data
    )
    {
        string sceneName = GetString(data, "sceneName");
        if (sceneName.Length == 0)
            return null;

        return new()
        {
            BroadcasterId = broadcasterId,
            Kind = kind,
            SceneName = sceneName,
        };
    }

    private static ObsDashboardStateChangedEvent Output(
        Guid broadcasterId,
        string kind,
        JsonElement data
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            Kind = kind,
            Active = GetBool(data, "outputActive"),
            Paused =
                kind == ObsStateKinds.Record
                    ? GetString(data, "outputState")
                        .Contains("PAUSED", StringComparison.OrdinalIgnoreCase)
                    : null,
        };

    private static ObsDashboardStateChangedEvent? Input(
        Guid broadcasterId,
        string kind,
        JsonElement data
    )
    {
        string inputName = GetString(data, "inputName");
        if (inputName.Length == 0)
            return null;

        bool isMute = kind == ObsStateKinds.InputMute;
        return new()
        {
            BroadcasterId = broadcasterId,
            Kind = kind,
            InputName = inputName,
            Muted = isMute ? GetBool(data, "inputMuted") : null,
            VolumeDb = isMute ? null : GetDouble(data, "inputVolumeDb"),
        };
    }

    private static ObsDashboardStateChangedEvent? SceneList(Guid broadcasterId, JsonElement data)
    {
        if (
            !data.TryGetProperty("scenes", out JsonElement scenes)
            || scenes.ValueKind != JsonValueKind.Array
        )
            return null;

        List<string> names = [];
        foreach (JsonElement scene in scenes.EnumerateArray())
        {
            string name = GetString(scene, "sceneName");
            if (name.Length > 0)
                names.Add(name);
        }

        return new()
        {
            BroadcasterId = broadcasterId,
            Kind = ObsStateKinds.SceneList,
            SceneNames = names,
        };
    }

    private static ObsDashboardStateChangedEvent? Named(
        Guid broadcasterId,
        string kind,
        string name
    ) =>
        name.Length == 0
            ? null
            : new()
            {
                BroadcasterId = broadcasterId,
                Kind = kind,
                Name = name,
            };

    private static ObsDashboardStateChangedEvent? NameList(
        Guid broadcasterId,
        string kind,
        JsonElement data,
        string property
    )
    {
        if (
            !data.TryGetProperty(property, out JsonElement items)
            || items.ValueKind != JsonValueKind.Array
        )
            return null;

        List<string> names = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            string name = item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
            if (name.Length > 0)
                names.Add(name);
        }

        return new()
        {
            BroadcasterId = broadcasterId,
            Kind = kind,
            Names = names,
        };
    }

    private static ObsDashboardStateChangedEvent? SceneItem(Guid broadcasterId, JsonElement data)
    {
        if (
            !data.TryGetProperty("sceneItemId", out JsonElement id)
            || !id.TryGetInt32(out int sceneItemId)
        )
            return null;

        return new()
        {
            BroadcasterId = broadcasterId,
            Kind = ObsStateKinds.SceneItemVisibility,
            SceneName = GetString(data, "sceneName"),
            SceneItemId = sceneItemId,
            Enabled = GetBool(data, "sceneItemEnabled"),
        };
    }

    private static bool GetBool(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double? GetDouble(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
