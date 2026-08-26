// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// <see cref="IResourceQuotaService"/> stubs for creation-path quota tests, mirroring the real
/// <c>ResourceQuotaService</c> decision shape without depending on the registry's real (generous)
/// baselines. <see cref="Unlimited"/> mirrors every key resolving -1; <see cref="WithLimit"/> layers one
/// finite cap on top.
/// </summary>
internal static class TestQuota
{
    public static IResourceQuotaService Unlimited()
    {
        IResourceQuotaService quota = Substitute.For<IResourceQuotaService>();
        quota
            .CheckAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                Result.Success(
                    new QuotaCheckDto(
                        true,
                        callInfo.ArgAt<string>(1),
                        callInfo.ArgAt<long>(2),
                        -1,
                        -1
                    )
                )
            );
        return quota;
    }

    public static IResourceQuotaService WithLimit(string limitKey, long value)
    {
        IResourceQuotaService quota = Unlimited();
        quota
            .CheckAsync(Arg.Any<Guid>(), limitKey, Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                long resultingCount = callInfo.ArgAt<long>(2);
                bool allowed = resultingCount <= value;
                return Result.Success(
                    new QuotaCheckDto(
                        allowed,
                        limitKey,
                        resultingCount,
                        value,
                        Math.Max(0, value - resultingCount)
                    )
                );
            });
        return quota;
    }
}
