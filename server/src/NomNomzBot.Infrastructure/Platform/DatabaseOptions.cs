// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = null!;
    public int MaxPoolSize { get; set; } = 20;
    public int MinPoolSize { get; set; } = 5;
}
