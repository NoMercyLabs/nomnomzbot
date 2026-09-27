// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
