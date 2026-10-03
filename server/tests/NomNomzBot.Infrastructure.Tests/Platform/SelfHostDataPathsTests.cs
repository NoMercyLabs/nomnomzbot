// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Data.Sqlite;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// The production SQLite connection string must keep Microsoft.Data.Sqlite pooling off: the pool can hand one
/// native handle to two connections when several threads open at once, which fails as SQLite Error 5 on close.
/// </summary>
public sealed class SelfHostDataPathsTests
{
    [Fact]
    public void Production_sqlite_connection_string_has_pooling_off_and_targets_the_database_file()
    {
        SqliteConnectionStringBuilder builder = new(SelfHostDataPaths.SqliteConnectionString);

        builder.Pooling.Should().BeFalse();
        builder.DataSource.Should().Be(SelfHostDataPaths.DatabaseFile);
    }
}
