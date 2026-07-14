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
using NomNomzBot.Infrastructure.Widgets.Bundling;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Warms one real <see cref="JintVueSfcCompiler"/> once per test class (loading the ~778 kb compiler bundle into a
/// Jint engine is expensive), so the Vue compile tests share a single engine pool instead of paying warm-up each.
/// </summary>
public sealed class VueSfcCompilerFixture : IDisposable
{
    public JintVueSfcCompiler Compiler { get; } =
        new(new ConfigurationBuilder().Build(), NullLogger<JintVueSfcCompiler>.Instance);

    public void Dispose() => Compiler.Dispose();
}
