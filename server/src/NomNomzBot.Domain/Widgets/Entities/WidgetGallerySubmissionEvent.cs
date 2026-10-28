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
