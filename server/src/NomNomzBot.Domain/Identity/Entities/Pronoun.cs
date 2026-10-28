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

namespace NomNomzBot.Domain.Identity.Entities;

public class Pronoun
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; } = null!;

    // The alejo.io identifier (e.g. "theythem", "sheher") — null for combination pronouns that
    // alejo generates from pairs (she/they, he/she, he/they) and does not return in its catalog.
    // Used to map a viewer's alejo API response back to an R.1 grammar row.
    [MaxLength(30)]
    public string? Key { get; set; }

    [MaxLength(20)]
    public string Subject { get; set; } = null!;

    [MaxLength(20)]
    public string Object { get; set; } = null!;

    // Possessive determiner used adjectivally in grammar templates (e.g. "check out {possessive} stream").
    [MaxLength(20)]
    public string Possessive { get; set; } = null!;

    // Neutral noun for the "{genderedTerm}" template variable (e.g. "guy"/"gal"/"person").
    [MaxLength(20)]
    public string GenderedTerm { get; set; } = null!;

    public bool Singular { get; set; }
}
