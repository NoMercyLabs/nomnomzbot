// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Persistence;

/// <summary>
/// Npgsql runs with <c>EnableRetryOnFailure</c>, and its retrying execution strategy REFUSES a
/// user-initiated <c>BeginTransactionAsync</c>: the call throws "does not support user-initiated
/// transactions" the moment a query runs. Development and every test here use SQLite, which has no
/// retrying strategy — so such a call site is invisible locally and only detonates in a deployed
/// Postgres environment. On 2026-08-23 that took the API down at boot: the content seeder opened a
/// bare transaction, seeding threw, and the process exited before serving a request.
/// <para>
/// The mechanism that makes it safe is <c>RetriableTransaction</c> / <c>UnitOfWork</c>
/// <c>ExecuteInTransactionAsync</c>, which runs the whole transaction through the provider's
/// execution strategy. This test is the gate that keeps a new bare call from creeping back in — no
/// runtime test can catch it, because the failure needs Postgres.
/// </para>
/// </summary>
public sealed class RetriableTransactionGuardTests
{
    /// <summary>The two files that legitimately open a transaction — they ARE the mechanism.</summary>
    private static readonly string[] Sanctioned =
    [
        Path.Combine("Platform", "Persistence", "RetriableTransaction.cs"),
        Path.Combine("Platform", "Persistence", "UnitOfWork.cs"),
    ];

    [Fact]
    public void No_production_file_opens_a_transaction_outside_the_retriable_mechanism()
    {
        string infrastructureRoot = InfrastructureSourceRoot();

        List<string> offenders =
        [
            .. Directory
                .EnumerateFiles(infrastructureRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Where(file =>
                    !Sanctioned.Any(s => file.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                )
                .Where(file =>
                    Regex.IsMatch(File.ReadAllText(file), @"\bBeginTransactionAsync\s*\(")
                )
                .Select(file => Path.GetRelativePath(infrastructureRoot, file)),
        ];

        offenders
            .Should()
            .BeEmpty(
                "a bare BeginTransactionAsync throws under Npgsql's retrying execution strategy — "
                    + "route it through IUnitOfWork.ExecuteInTransactionAsync or the DbContext "
                    + "ExecuteInTransactionAsync extension instead"
            );
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    /// <summary>Walks up from the test assembly to the repo's <c>server/</c> folder — the test binary
    /// lives under <c>tests/&lt;project&gt;/bin/…</c>, so the source tree is always a fixed walk away.</summary>
    private static string InfrastructureSourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Directory
            .Exists(Path.Combine(directory?.FullName ?? string.Empty, "src"))
            .Should()
            .BeTrue("the test must be able to find the server source tree to scan it");

        return Path.Combine(directory!.FullName, "src", "NomNomzBot.Infrastructure");
    }
}
