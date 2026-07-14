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
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Application service for managing overlay widgets (alerts, chat overlays, goals, etc.).
/// </summary>
public interface IWidgetService
{
    /// <summary>Create a new widget.</summary>
    Task<Result<WidgetDetail>> CreateAsync(
        string broadcasterId,
        CreateWidgetRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update an existing widget.</summary>
    Task<Result<WidgetDetail>> UpdateAsync(
        string broadcasterId,
        string widgetId,
        UpdateWidgetRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete a widget.</summary>
    Task<Result> DeleteAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List all widgets for a channel with pagination.</summary>
    Task<Result<PagedList<WidgetDetail>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a single widget by ID.</summary>
    Task<Result<WidgetDetail>> GetAsync(
        string broadcasterId,
        string widgetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get a widget by its public access token (for overlay URLs).</summary>
    Task<Result<WidgetDetail>> GetByTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Compile-on-save: append the widget's next <c>WidgetVersion</c>, build it, and (on success) point the widget
    /// at it. A failed build is a persisted <c>error</c> version, not a discard; the returned detail carries the
    /// build status either way, and the build lifecycle event is published for the overlay/editor to react to.
    /// </summary>
    Task<Result<WidgetVersionDetail>> CompileAsync(
        string broadcasterId,
        string widgetId,
        CompileWidgetRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>List a widget's version history, newest first.</summary>
    Task<Result<PagedList<WidgetVersionSummary>>> ListVersionsAsync(
        string broadcasterId,
        string widgetId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get one version in full (source + build log), for rollback/debug.</summary>
    Task<Result<WidgetVersionDetail>> GetVersionAsync(
        string broadcasterId,
        string widgetId,
        string versionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Re-point the widget's active version at an earlier <b>successful</b> build (no recompile) and publish a
    /// cache-bust reload. Fails if the target version is not a successful build.
    /// </summary>
    Task<Result<WidgetDetail>> RollbackAsync(
        string broadcasterId,
        string widgetId,
        string versionId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Record an overlay-reported runtime fault against the widget (audit B5); no event.</summary>
    Task<Result> RecordRuntimeErrorAsync(
        string broadcasterId,
        string widgetId,
        string error,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The public, token-resolved overlay manifest: the channel's enabled, successfully-built widgets with their
    /// bundle URLs, hashes, trust tiers, and settings. Token-auth only (never the user JWT); resolves the channel
    /// by <c>Channels.OverlayToken</c>. This read intentionally bypasses the tenant query filter (the caller is
    /// anonymous) and scopes to the resolved channel explicitly.
    /// </summary>
    Task<Result<OverlayManifest>> GetOverlayManifestAsync(
        string overlayToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Serve one widget's active compiled bundle by overlay token — the content the host page injects. Fails unless
    /// the widget is enabled, owned by the token's channel, and its active version built successfully.
    /// </summary>
    Task<Result<OverlayBundle>> GetOverlayBundleAsync(
        string overlayToken,
        string widgetId,
        CancellationToken cancellationToken = default
    );
}
