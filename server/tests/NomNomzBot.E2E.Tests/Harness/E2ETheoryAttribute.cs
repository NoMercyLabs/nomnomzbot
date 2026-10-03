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
/// The <see cref="TheoryAttribute"/> twin of <see cref="E2EFactAttribute"/>: skips itself unless the harness is
/// explicitly enabled (<c>NOMNOMZ_E2E=1</c>), so the Playwright fixture never launches in CI.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2ETheoryAttribute : TheoryAttribute
{
    public E2ETheoryAttribute()
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
