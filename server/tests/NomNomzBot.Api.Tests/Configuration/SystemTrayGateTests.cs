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
using NomNomzBot.Api.Configuration;
using NomNomzBot.Domain.Enums.Deployment;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the system-tray gate truth table: the tray shows iff ALL of self-host mode, Windows, and an interactive
/// session (<c>Environment.UserInteractive</c>) hold. Any single false (SaaS, non-Windows, or a non-interactive
/// service/headless session) suppresses it. The OS and interactive signals are injected so the decision is asserted
/// without depending on the real host.
/// </summary>
public sealed class SystemTrayGateTests
{
    [Theory]
    // All three true — the only combination that shows the tray.
    [InlineData(DeploymentMode.SelfHostLite, true, true, true)]
    [InlineData(DeploymentMode.SelfHostFull, true, true, true)]
    // SaaS is never eligible, even on an interactive Windows desktop.
    [InlineData(DeploymentMode.Saas, true, true, false)]
    // Self-host but not Windows — the tray is Win32-only.
    [InlineData(DeploymentMode.SelfHostLite, false, true, false)]
    [InlineData(DeploymentMode.SelfHostFull, false, true, false)]
    // Self-host on Windows but not interactive (a Windows Service / session-0 / headless container).
    [InlineData(DeploymentMode.SelfHostLite, true, false, false)]
    [InlineData(DeploymentMode.SelfHostFull, true, false, false)]
    // Every other "two of three" combination is still off.
    [InlineData(DeploymentMode.Saas, false, false, false)]
    public void ShouldShowTray_follows_the_self_host_and_windows_and_interactive_truth_table(
        DeploymentMode mode,
        bool isWindows,
        bool isUserInteractive,
        bool expected
    )
    {
        bool result = SystemTrayGate.ShouldShowTray(mode, isWindows, isUserInteractive);

        result.Should().Be(expected);
    }
}
