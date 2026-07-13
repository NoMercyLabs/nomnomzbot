// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Widgets.Entities;

/// <summary>
/// An immutable review/pin-change record for a <see cref="WidgetGalleryItem"/> (schema §P.9, GLOBAL, APPEND-ONLY).
/// Every status transition and re-pin appends one row, giving the gallery a tamper-evident moderation history.
/// </summary>
public class WidgetGallerySubmissionEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid GalleryItemId { get; set; }

    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;

    public Guid? ChangedByUserId { get; set; }
    public string? NewPinnedCommitSha { get; set; }
    public string? Note { get; set; }

    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
