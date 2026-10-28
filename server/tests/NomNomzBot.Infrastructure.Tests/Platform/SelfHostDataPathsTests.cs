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
