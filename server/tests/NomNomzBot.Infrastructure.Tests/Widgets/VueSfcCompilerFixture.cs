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
