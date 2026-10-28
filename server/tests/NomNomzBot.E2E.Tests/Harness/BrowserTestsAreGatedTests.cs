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

using System.Reflection;
using Microsoft.Playwright.Xunit;

namespace NomNomzBot.E2E.Tests.Harness;

/// <summary>
/// CI has no browser. A plain [Fact] on a browser test class still runs the class setup, which launches the
/// browser and fails the build (CI run 37557236095). A check that needs no browser belongs in a plain class.
/// </summary>
public sealed class BrowserTestsAreGatedTests
{
    [Fact]
    public void Every_test_on_a_browser_class_is_skipped_when_e2e_is_off()
    {
        List<string> ungated = typeof(BrowserTestsAreGatedTests)
            .Assembly.GetTypes()
            .Where(type => typeof(PageTest).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method =>
                method
                    .GetCustomAttributes<FactAttribute>()
                    .Any(attribute =>
                        attribute is not E2EFactAttribute and not E2ETheoryAttribute
                        && attribute.Skip is null
                    )
            )
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.Empty(ungated);
    }
}
