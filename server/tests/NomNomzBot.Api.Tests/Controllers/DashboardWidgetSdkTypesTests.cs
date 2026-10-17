// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The dashboard type-checks the widget SFCs its specs import (<c>raid_recap.vue</c>), so it carries the widget SDK
/// typings as <c>dashboard/src/api/nnz-widget.d.ts</c>. The emitter, wired like production (event registry and
/// every pipeline action), is the source: this test fails when the file differs from what the emitter writes now.
/// Set <c>NNZ_WRITE_SDK_TYPES=1</c> and run it to rewrite the file.
/// </summary>
public sealed class DashboardWidgetSdkTypesTests
{
    [Fact]
    public void The_dashboard_widget_typings_match_what_the_emitter_writes()
    {
        string path = Path.Combine(RepoRoot(), "dashboard", "src", "api", "nnz-widget.d.ts");
        string emitted = Emit().ReplaceLineEndings("\n").TrimEnd() + "\n";

        if (Environment.GetEnvironmentVariable("NNZ_WRITE_SDK_TYPES") == "1")
            File.WriteAllText(path, emitted);

        File.Exists(path)
            .Should()
            .BeTrue("the typings are generated: run with NNZ_WRITE_SDK_TYPES=1");
        File.ReadAllText(path)
            .ReplaceLineEndings("\n")
            .Should()
            .Be(emitted, "the file is generated: rerun with NNZ_WRITE_SDK_TYPES=1 to refresh it");
    }

    private static string Emit()
    {
        ServiceCollection services = new();
        services.AddImplementationsOf<ICommandAction>(
            typeof(SdkTypeEmitter).Assembly,
            ServiceLifetime.Transient,
            typeof(CapturingCommandAction)
        );
        List<ICommandAction> actions = services
            .Select(d =>
                (ICommandAction)RuntimeHelpers.GetUninitializedObject(d.ImplementationType!)
            )
            .ToList();

        return new SdkTypeEmitter(
            new EventCatalog(),
            actions: actions,
            widgetEvents: new WidgetEventPayloadRegistry()
        ).EmitTypeScript(SdkContext.Widget);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "dashboard", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}."
        );
    }
}
