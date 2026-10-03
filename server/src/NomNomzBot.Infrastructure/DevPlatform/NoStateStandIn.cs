// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Stands in for a translator dependency that holds runtime state (the channel registry, the self-echo guard).
/// Every member returns its default: <c>null</c>, <c>false</c>, an already-completed task. That is exactly the
/// "nothing is known about this channel" answer a translator already handles. It lets a real translator turn a
/// wire fixture into its domain event without a database or a live channel, and it never supplies a value.
/// </summary>
public class NoStateStandIn : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Type? returnType = targetMethod?.ReturnType;
        if (returnType is null || returnType == typeof(void))
            return null;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return default(ValueTask);
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            Type result = returnType.GetGenericArguments()[0];
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(result)
                .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null]);
        }
        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
