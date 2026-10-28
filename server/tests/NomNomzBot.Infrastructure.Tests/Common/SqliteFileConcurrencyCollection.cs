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

namespace NomNomzBot.Infrastructure.Tests.Common;

/// <summary>
/// S119: the S004 concurrency proofs (<c>CurrencyBalanceConcurrencyTests</c>,
/// <c>CatalogStockConcurrencyTests</c>, <c>UsageMeteringConcurrencyTests</c>,
/// <c>WidgetGalleryInstallConcurrencyTests</c>) each spin up 8-20 real concurrent
/// <see cref="Microsoft.Data.Sqlite.SqliteConnection"/>s against a WAL-mode file database from separate
/// <see cref="System.Threading.Tasks.Task"/>s. Running several of those classes at once (xUnit's default
/// cross-class parallelism) stacks their thread-pool pressure and native SQLite handle churn on top of each
/// other, which was implicated in the intermittent test-host crash tracked as S119. Placing them in one
/// non-parallel collection serializes them against EACH OTHER (never against themselves — each test still
/// exercises real concurrent writers) without weakening what any single test proves.
/// </summary>
[CollectionDefinition("SqliteFileConcurrency", DisableParallelization = true)]
public sealed class SqliteFileConcurrencyCollection { }
