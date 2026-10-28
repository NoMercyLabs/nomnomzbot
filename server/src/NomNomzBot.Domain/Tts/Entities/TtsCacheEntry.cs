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
