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

namespace NomNomzBot.Domain.Tts.Entities;

public class TtsCacheEntry : BaseEntity
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string ContentHash { get; set; } = null!;

    public byte[] AudioData { get; set; } = null!;

    public int DurationMs { get; set; }

    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    [MaxLength(255)]
    public string VoiceId { get; set; } = null!;
}
