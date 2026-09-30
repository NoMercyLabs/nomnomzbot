// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.PlatformContent.Entities;

/// <summary>
/// One audio file the platform owns — no channel owns it. A <c>sound_clip</c> template names it by id, and
/// installing that template copies the bytes into the installing channel's own clip storage, so a channel
/// never reads this row or its file at run time. NOT <c>ITenantScoped</c>. A file that a non-retired
/// template still names cannot be deleted.
/// </summary>
public class PlatformAudioAsset : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The label admins pick the file by in the template form.</summary>
    [MaxLength(100)]
    public string DisplayName { get; set; } = null!;

    /// <summary>The file name as uploaded; shown in the admin list and reused as the name hint on install.</summary>
    [MaxLength(200)]
    public string FileName { get; set; } = null!;

    /// <summary>Opaque key in the platform area of the sound clip store.</summary>
    [MaxLength(200)]
    public string StorageKey { get; set; } = null!;

    /// <summary><c>audio/mpeg</c>, <c>audio/ogg</c> or <c>audio/wav</c> — content-sniffed, never taken from the request.</summary>
    [MaxLength(40)]
    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public int DurationMs { get; set; }

    /// <summary>Lower-case hex SHA-256 of the stored bytes.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = null!;

    /// <summary>The platform IAM principal who uploaded the file.</summary>
    public Guid UploadedByPrincipalId { get; set; }
}
