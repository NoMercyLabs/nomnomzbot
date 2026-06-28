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

    public bool Singular { get; set; }
}
