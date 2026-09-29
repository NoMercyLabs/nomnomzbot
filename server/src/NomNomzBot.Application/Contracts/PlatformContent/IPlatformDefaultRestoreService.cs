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

namespace NomNomzBot.Application.Contracts.PlatformContent;

/// <summary>
/// Puts ONE channel's copy of platform content (a seeded system pipeline such as the raid flow, or a timer
/// installed from a template) back on the platform default — the definition's current published version. The
/// preview names every difference first, so the streamer sees what a restore replaces before confirming.
/// Supported kinds: <c>pipeline</c> and <c>timer</c>.
/// </summary>
public interface IPlatformDefaultRestoreService
{
    /// <summary>What a restore of this row would change. <c>NOT_PLATFORM_CONTENT</c> when the channel made
    /// the row itself.</summary>
    Task<Result<PlatformDefaultPreviewDto>> PreviewAsync(
        Guid broadcasterId,
        string kind,
        Guid rowId,
        CancellationToken ct = default
    );

    /// <summary>Restores the row and returns the fresh preview (no changes left on success).</summary>
    Task<Result<PlatformDefaultPreviewDto>> RestoreAsync(
        Guid broadcasterId,
        string kind,
        Guid rowId,
        CancellationToken ct = default
    );
}

/// <summary>
/// The consequence of restoring one row to its platform default. <see cref="InstalledVersion"/> is the
/// version the row was installed or last synced from; <see cref="DefaultVersion"/> is what a restore writes.
/// <see cref="IsEdited"/> is true when the channel changed the row since then. <see cref="Changes"/> lists
/// each part that differs from the default — empty means a restore changes nothing.
/// </summary>
public sealed record PlatformDefaultPreviewDto(
    string Kind,
    Guid RowId,
    string DefaultName,
    int? InstalledVersion,
    int DefaultVersion,
    bool IsEdited,
    IReadOnlyList<PlatformDefaultChangeDto> Changes
);

/// <summary>
/// One part of a row that a restore replaces. <see cref="Field"/> is a stable key the dashboard labels
/// (timer: <c>name</c>, <c>messages</c>, <c>interval</c>, <c>min_chat_activity</c>, <c>enabled</c>,
/// <c>fire_once</c>; pipeline: <c>steps</c>, <c>step_settings</c>); the values are shown as-is.
/// </summary>
public sealed record PlatformDefaultChangeDto(string Field, string Current, string Default);
