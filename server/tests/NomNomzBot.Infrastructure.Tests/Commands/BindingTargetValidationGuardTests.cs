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
