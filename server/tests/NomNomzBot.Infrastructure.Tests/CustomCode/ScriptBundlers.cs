// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// The script bundler the tests build with. <see cref="Real"/> runs the real esbuild binary: CI installs it on PATH,
/// and a local run points <c>Widgets__EsbuildPath</c> at a downloaded binary.
/// </summary>
internal static class ScriptBundlers
{
    public static EsbuildScriptBundler Real() => With(new ProcessRunner());

    public static EsbuildScriptBundler With(IProcessRunner runner) =>
        new(
            runner,
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            NullLogger<EsbuildScriptBundler>.Instance
        );
}
