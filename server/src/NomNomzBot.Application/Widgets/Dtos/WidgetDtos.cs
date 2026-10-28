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

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// A widget's full detail. The authored source and compiled bundle are NOT here — they live on append-only
/// <c>WidgetVersion</c> rows; <see cref="ActiveVersionId"/> names the version the overlay currently serves.
/// <c>Framework</c> is the source language (vue|vanilla; react and svelte are refused); <c>Source</c> is the provenance
/// (first_party|verified_gallery|custom). <see cref="GalleryUpdateAvailable"/> is true when
/// <see cref="GalleryItemId"/> is set and the gallery item's source has moved on since this widget was installed
/// or last updated from it — the streamer's clone is running stale code. Always false for a self-authored
/// <c>custom</c> widget (no gallery link to compare against). <see cref="IsAttached"/> is true when a live
/// browser source has this widget open right now (<c>IOverlayPresenceRegistry</c>) — the "overlay last-seen"
/// signal, so a streamer can tell an overlay is actually loaded in OBS without firing a test event.
/// <see cref="IsCustomized"/> is true when the channel has saved its own code over a catalogue widget's source
/// (a first-party system widget or a gallery install) — the dashboard marks it "Edited" and offers the reset to
/// the catalogue source; a catalogue update never overwrites it on its own. Always false for a <c>custom</c>
/// widget, which has no catalogue source to differ from.
/// </summary>
public sealed record WidgetDetail(
    Guid Id,
    string Name,
    string? Description,
    string Framework,
    string Source,
    bool IsEnabled,
    string? OverlayUrl,
    Guid? ActiveVersionId,
    Guid? GalleryItemId,
    Dictionary<string, object?> Settings,
    List<string> EventSubscriptions,
    string? LastRuntimeError,
    DateTime? LastRanAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool GalleryUpdateAvailable,
    bool IsAttached,
    bool IsCustomized,
    ObsBrowserSource? ObsSetup = null
);

/// <summary>
/// How a streamer sets up one overlay as an OBS browser source: the width and height to enter, whether to tick
/// "Control audio via OBS", and whether to tick "Shutdown source when not visible" (OBS then unloads the page, so
/// anything that must keep listening or playing leaves it off). Authored once per catalogue widget; a widget with no
/// catalogue entry (custom, community) carries none.
/// </summary>
public sealed record ObsBrowserSource(
    int Width,
    int Height,
    bool ControlAudioViaObs,
    bool ShutdownWhenHidden
);

/// <summary>
/// One channel-owned system surface (widgets-overlays.md §1.2) for the OBS page's Required overlays panel.
/// <see cref="Key"/> is the surface's stable catalogue key (<c>audio</c>, <c>alerts</c>, ...) so the client can
/// name and describe it in the viewer's language; <see cref="Widget"/> is the provisioned widget.
/// </summary>
public sealed record RequiredOverlay(string Key, WidgetDetail Widget);

/// <summary>
/// Result of rotating one widget's overlay token (audit B5). <see cref="PreviousUrl"/> stays live until
/// <see cref="GraceExpiresAt"/> — the dashboard shows both so the streamer can re-copy <see cref="NewUrl"/> into
/// OBS before the old one stops working, instead of the source going blank with no warning.
/// </summary>
public sealed record WidgetTokenRotationResult(
    Guid WidgetId,
    string PreviousUrl,
    string NewUrl,
    DateTimeOffset GraceExpiresAt
);

public sealed record CreateWidgetRequest
{
    public required string Name { get; init; }

    /// <summary>Source language of the widget: <c>vanilla</c> | <c>vue</c>.</summary>
    public required string Framework { get; init; }

    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
}

public sealed record UpdateWidgetRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
    public bool? IsEnabled { get; init; }
}

/// <summary>The authored source to compile-on-save into the widget's next append-only version.</summary>
public sealed record CompileWidgetRequest
{
    public required string SourceCode { get; init; }
}

/// <summary>
/// Fork source for clone-to-edit — exactly one of <see cref="GalleryItemId"/> / <see cref="InstalledWidgetId"/> is
/// set. Produces a NEW, fully-owned <c>custom</c> widget with the source copied in (independently editable).
/// </summary>
public sealed record CloneWidgetRequest
{
    public Guid? GalleryItemId { get; init; }
    public Guid? InstalledWidgetId { get; init; }
}

/// <summary>
/// The public, token-resolved overlay manifest (widgets-overlays.md §4). The single read an OBS browser source
/// needs: the channel's enabled, successfully-built widgets, each with the URL to fetch its compiled bundle, the
/// content hash (cache-bust key), its render-time trust tier, and its live settings + event subscriptions.
/// </summary>
public sealed record OverlayManifest(
    Guid ChannelId,
    string CspNonce,
    List<OverlayWidgetEntry> Widgets
);

/// <summary>
/// One widget in the overlay manifest. <see cref="TrustTier"/> (first_party | verified_community | unverified) is
/// derived from the widget's <c>Source</c> and drives the render-time CSP tier — a self-authored (custom) widget is
/// always <c>unverified</c> (fail-closed).
/// <see cref="GalleryKey"/> is the natural key of the gallery item the widget was made from (null for a widget with
/// none); it is how the host page recognises the system Audio widget without trusting a user setting.
/// </summary>
public sealed record OverlayWidgetEntry(
    Guid WidgetId,
    string Name,
    string Framework,
    string TrustTier,
    string BundleUrl,
    string ContentHash,
    List<string> EventSubscriptions,
    Dictionary<string, object?> Settings,
    string? GalleryKey = null
);

/// <summary>A widget's compiled bundle, served to the overlay host page. <see cref="Framework"/> picks the MIME type.</summary>
public sealed record OverlayBundle(string Content, string Framework, string ContentHash);

/// <summary>
/// What an overlay browser-source token actually authenticates: always a channel, and — when the token is a
/// widget's OWN <c>OverlayToken</c> (or its still-grace-windowed <c>PreviousOverlayToken</c>) rather than the
/// legacy channel-wide <c>Channel.OverlayToken</c> — the single widget it was minted for. A null
/// <see cref="WidgetId"/> means the token is channel-wide and may see every widget; a non-null one confines the
/// caller to that widget alone (the fix for one leaked widget token unlocking the whole channel's manifest).
/// <see cref="Token"/> is the widget token that was presented (null for a channel-wide token); the overlay hub
/// keeps it so a later rotation can close the pages that still use a retired token.
/// </summary>
public sealed record OverlayTokenScope(Guid BroadcasterId, Guid? WidgetId, string? Token = null);

/// <summary>
/// A starter widget template the editor offers when creating a new custom widget — a working, SDK-using starting
/// point (never a blank editor). <see cref="Source"/> is the authored source to seed into the editor + compile.
/// </summary>
public sealed record WidgetTemplate(
    string Key,
    string Name,
    string Description,
    string Framework,
    string Source
);

/// <summary>A row in a widget's version history (rollback / debug list).</summary>
public sealed record WidgetVersionSummary(
    Guid Id,
    int VersionNumber,
    string BuildStatus,
    string? ContentHash,
    DateTime? CompiledAt,
    DateTime CreatedAt
);

/// <summary>
/// A single widget version in full. Carries <see cref="SourceCode"/> so the editor can load the current source to
/// edit, and <see cref="BuildError"/>/<see cref="BuildLog"/> so a failed build is inspectable. The compiled bundle
/// itself is served to overlays via the manifest, not returned here.
/// </summary>
public sealed record WidgetVersionDetail(
    Guid Id,
    Guid WidgetId,
    int VersionNumber,
    string BuildStatus,
    string? SourceCode,
    string? BuildError,
    string? BuildLog,
    string? ContentHash,
    DateTime? CompiledAt,
    DateTime CreatedAt
);
