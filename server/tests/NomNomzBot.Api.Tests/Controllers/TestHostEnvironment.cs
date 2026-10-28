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

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>An <see cref="IHostEnvironment"/> a controller test can pin to a named environment.</summary>
internal sealed class TestHostEnvironment : IHostEnvironment
{
    public static TestHostEnvironment Development { get; } = new(Environments.Development);

    public static TestHostEnvironment Production { get; } = new(Environments.Production);

    private TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "NomNomzBot.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
