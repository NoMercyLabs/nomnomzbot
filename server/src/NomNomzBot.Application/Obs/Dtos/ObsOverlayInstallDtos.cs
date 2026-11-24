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

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Obs.Dtos;

/// <summary>
/// One OBS browser source to create or update (OBS-WS <c>CreateInput</c> / <c>SetInputSettings</c> with input kind
/// <c>browser_source</c>). <see cref="SceneName"/> is the scene a new source is created in; an existing source
/// keeps its place and only has its settings rewritten.
/// </summary>
public sealed record ObsBrowserSourceSpec(
    string SceneName,
    string InputName,
    string Url,
    int Width,
    int Height,
    bool RerouteAudio,
    bool ShutdownWhenHidden
);

/// <summary>What a browser-source write did.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObsBrowserSourceOutcome
{
    /// <summary>OBS had no source of that name; one was created.</summary>
    Created,

    /// <summary>OBS already had the source; its settings were rewritten.</summary>
    Updated,
}

/// <summary>REST body for the overlay install route: the scene to add the overlays to and the overlays picked.</summary>
/// <summary>
/// Where the person puts one overlay: the scene they picked and the source name they typed. A null or blank name
/// uses the overlay's own name.
/// </summary>
public sealed record ObsOverlayPlacement(string Key, string SceneName, string? SourceName);

/// <summary>
/// With <see cref="Placements"/>, each overlay goes where the person put it and no scene is made. Without it, the
/// older call shape still works: one nested scene, added to <see cref="SceneName"/>.
/// </summary>
public sealed record ObsOverlayInstallRequest(
    string SceneName,
    IReadOnlyList<string> OverlayKeys,
    IReadOnlyList<ObsOverlayPlacement>? Placements = null
);

/// <summary>How one picked overlay ended up in OBS.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObsOverlayInstallOutcome
{
    Created,
    Updated,
    Failed,
}

/// <summary>
/// The result for one picked overlay. The overlay address (it carries the secret token) never appears here.
/// </summary>
public sealed record ObsOverlayInstallEntry(
    string Key,
    string SourceName,
    ObsOverlayInstallOutcome Outcome,
    string? Error
);

/// <summary>
/// The result of one install call: the nested scene that holds the overlays, whether it was added to the picked
/// scene by this call, and one entry per picked overlay.
/// </summary>
public sealed record ObsOverlayInstallResult(
    string? NestedSceneName,
    bool AddedToScene,
    IReadOnlyList<ObsOverlayInstallEntry> Overlays
);
