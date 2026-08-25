// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
namespace NomNomzBot.Domain.Scratch;

public sealed class CleanupProbeFilter
{
    public string Kind { get; init; } = string.Empty;
    public bool Templated { get; init; }
}

public sealed class CleanupProbe
{
    public string? Status { get; init; }

    public string Describe(CleanupProbeFilter? f)
    {
        if (f != null && f.Kind == "system" && !f.Templated)
        {
            return "system-non-templated";
        }

        return Status!!.ToUpperInvariant();
    }
}
