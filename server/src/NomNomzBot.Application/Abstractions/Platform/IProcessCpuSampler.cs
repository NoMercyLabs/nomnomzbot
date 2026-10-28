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

namespace NomNomzBot.Application.Abstractions.Platform;

/// <summary>
/// The share of the machine's processor time this process used since the previous sample, as a percentage
/// of all cores. The admin console's System panel reads it; it used to show a hardcoded zero.
/// </summary>
public interface IProcessCpuSampler
{
    /// <summary>
    /// Percent of total processor capacity consumed since the previous call, 0–100. The first call has no
    /// earlier sample to measure against and reports 0.
    /// </summary>
    double SamplePercent();
}
