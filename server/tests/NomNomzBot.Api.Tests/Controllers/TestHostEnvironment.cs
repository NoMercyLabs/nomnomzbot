// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
