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
