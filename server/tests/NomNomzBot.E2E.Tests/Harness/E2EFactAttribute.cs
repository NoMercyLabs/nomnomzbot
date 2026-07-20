// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.E2E.Tests.Harness;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself unless the harness is explicitly enabled
/// (<c>NOMNOMZ_E2E=1</c>). Because a statically-skipped fact is never constructed, the Playwright browser
/// fixture never launches — so <c>dotnet test</c> in CI stays green with no live server reachable and no
/// <c>playwright install</c> step. Set the variable (and install a browser once) to run the tests for real.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!E2ESettings.Enabled)
        {
            Skip =
                $"E2E disabled. Set {E2ESettings.EnableVariable}=1 and install a browser once "
                + "(`pwsh bin/Debug/net10.0/playwright.ps1 install chromium`) to run against "
                + $"{E2ESettings.BaseUrl} (override with {E2ESettings.BaseUrlVariable}).";
        }
    }
}
