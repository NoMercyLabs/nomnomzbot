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
using Microsoft.Extensions.Hosting;
using NomNomzBot.Infrastructure.Stream.Jobs;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Guards the 2026-08-25 outage class: `catch (Exception ex) when (ex is not OperationCanceledException)`
/// looks like "let shutdown through", but <see cref="TaskCanceledException"/> — exactly what HttpClient
/// raises on its own timeout — DERIVES from <see cref="OperationCanceledException"/>, so that filter lets
/// a routine HTTP timeout escape the catch. Inside a <see cref="BackgroundService"/>, an escaped exception
/// hits <c>BackgroundServiceExceptionBehavior.StopHost</c> and kills the ENTIRE bot, not just one tick. One
/// slow Spotify call did exactly that on 2026-08-25 (5 crash-loop restarts, 502 dashboard).
/// <para>
/// The fix is to filter on the cancellation TOKEN (<c>!stoppingToken.IsCancellationRequested</c> or
/// whatever the local token is named) instead of the exception TYPE — only a genuinely cancelled token
/// means "we are shutting down".
/// </para>
/// <para>
/// <see cref="BackgroundService"/> types are discovered STRUCTURALLY — by reflection over the Infrastructure
/// assembly (which this test project already references) for every concrete type assignable to
/// <see cref="BackgroundService"/>, and by a source-pattern scan of the Api project tree for every class
/// declaration inheriting <c>BackgroundService</c> (the Infrastructure test project deliberately does not
/// take a project reference on Api just to reflect over it). Neither list is hand-maintained, so a NEW
/// service — in either project — is covered automatically the moment it exists.
/// </para>
/// </summary>
public sealed class BackgroundServiceCancellationFilterGuardTests
{
    private const string BadPattern = "is not OperationCanceledException";

    [Fact]
    public void No_BackgroundService_source_file_filters_on_the_exception_type_instead_of_the_token()
    {
        string serverRoot = ServerRoot();
        string infrastructureRoot = Path.Combine(serverRoot, "src", "NomNomzBot.Infrastructure");
        string apiRoot = Path.Combine(serverRoot, "src", "NomNomzBot.Api");

        // Reflection: every concrete BackgroundService in the assembly this test project already
        // references — the same universe AddHostedWorkers auto-discovers at startup.
        List<Type> infrastructureServices =
        [
            .. typeof(StreamStatusPollingService)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                    && typeof(BackgroundService).IsAssignableFrom(t)
                ),
        ];

        infrastructureServices
            .Should()
            .NotBeEmpty(
                "the reflection scan itself must find the known BackgroundService population"
            );

        List<string> offenders =
        [
            .. infrastructureServices
                .Select(t => SourceFileForType(infrastructureRoot, t))
                .Where(file => file is not null)
                .Cast<string>()
                .Distinct()
                .Where(FiltersOnExceptionType)
                .Select(file => Path.GetRelativePath(serverRoot, file)),
        ];

        // Api's BackgroundService types (e.g. AdminHubStatusPublisher) are discovered structurally too —
        // by their `: BackgroundService` source declaration — since this test project has no project
        // reference to NomNomzBot.Api to reflect over it directly.
        offenders.AddRange(
            Directory
                .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Where(DeclaresBackgroundServiceSubclass)
                .Where(FiltersOnExceptionType)
                .Select(file => Path.GetRelativePath(serverRoot, file))
        );

        offenders
            .Should()
            .BeEmpty(
                "a BackgroundService catch must filter on the cancellation TOKEN "
                    + "(`!stoppingToken.IsCancellationRequested` / `!ct.IsCancellationRequested`), never on "
                    + "`ex is not OperationCanceledException` — TaskCanceledException (an HttpClient timeout) "
                    + "derives from OperationCanceledException and would escape ExecuteAsync, taking the "
                    + "whole host down via BackgroundServiceExceptionBehavior.StopHost"
            );
    }

    private static bool FiltersOnExceptionType(string file) =>
        File.ReadAllText(file).Contains(BadPattern);

    // Allows an optional primary-constructor parameter list between the class name and the base-type
    // colon (`class Foo(...) : BackgroundService`), not just the plain `class Foo : BackgroundService`.
    private static bool DeclaresBackgroundServiceSubclass(string file) =>
        Regex.IsMatch(
            File.ReadAllText(file),
            @"\bclass\s+\w+[^{:]*:\s*(\w+\.)*BackgroundService\b"
        );

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    /// <summary>Finds the single source file under <paramref name="root"/> whose text declares
    /// <c>class {TypeName}</c> — reflection gives us the CLR type, this maps it back to the file to scan.</summary>
    private static string? SourceFileForType(string root, Type type)
    {
        Regex declaration = new($@"\bclass\s+{Regex.Escape(type.Name)}\b");
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .FirstOrDefault(file => declaration.IsMatch(File.ReadAllText(file)));
    }

    /// <summary>Walks up from the test assembly to the repo's <c>server/</c> folder — the test binary
    /// lives under <c>tests/&lt;project&gt;/bin/…</c>, so the source tree is always a fixed walk away.</summary>
    private static string ServerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        Directory
            .Exists(Path.Combine(directory?.FullName ?? string.Empty, "src"))
            .Should()
            .BeTrue("the test must be able to find the server source tree to scan it");

        return directory!.FullName;
    }
}
