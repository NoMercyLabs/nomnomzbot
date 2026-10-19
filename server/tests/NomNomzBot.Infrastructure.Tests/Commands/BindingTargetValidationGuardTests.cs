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

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Source-scan guard: binding-target validation lives in <c>BindingTargetBinder</c> only. A service that
/// defines its own <c>EnsureTargetValidAsync</c> or <c>ApplyTarget</c> is the hand-written form this blocks.
/// </summary>
public sealed class BindingTargetValidationGuardTests
{
    private static readonly string[] HandWrittenForms = ["EnsureTargetValidAsync", "ApplyTarget("];

    [Fact]
    public void No_service_but_the_binder_defines_its_own_target_validation()
    {
        string srcRoot = FindSrcRoot();
        string objSegment = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        List<string> offenders = [];
        foreach (
            string file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
        )
        {
            if (Path.GetFileName(file) == "BindingTargetBinder.cs" || file.Contains(objSegment))
                continue;

            string text = File.ReadAllText(file);
            if (HandWrittenForms.Any(form => text.Contains(form)))
                offenders.Add(Path.GetRelativePath(srcRoot, file));
        }

        offenders
            .Should()
            .BeEmpty("BindingTargetBinder is the only validator and applier of a binding target");
    }

    private static string FindSrcRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (
            dir is not null
            && !Directory.Exists(Path.Combine(dir.FullName, "src", "NomNomzBot.Infrastructure"))
        )
            dir = dir.Parent;

        return dir is null
            ? throw new DirectoryNotFoundException("server/src not found above the test output.")
            : Path.Combine(dir.FullName, "src");
    }
}
