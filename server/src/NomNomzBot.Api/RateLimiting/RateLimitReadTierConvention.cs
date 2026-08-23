// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// Splits GET/HEAD actions onto the generous "read" tier instead of the "write-cheap" tier every
/// controller inherits from <c>BaseController</c> (S114) — a dashboard's background polling must never
/// share a budget with that same caller's writes, or a poll storm can 429 a harmless toggle.
///
/// Only applies to controllers that rely on the inherited default. A controller that declares its own
/// <c>[EnableRateLimiting]</c>/<c>[DisableRateLimiting]</c> directly (anonymous public surfaces, the
/// platform-admin controllers, auth/device-poll actions) already made an explicit tier choice and is
/// left alone — same for any action that already carries its own explicit attribute.
/// </summary>
public sealed class RateLimitReadTierConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        bool controllerHasOwnPolicy =
            controller.ControllerType.IsDefined(typeof(EnableRateLimitingAttribute), inherit: false)
            || controller.ControllerType.IsDefined(
                typeof(DisableRateLimitingAttribute),
                inherit: false
            );

        if (controllerHasOwnPolicy)
            return;

        foreach (ActionModel action in controller.Actions)
        {
            bool actionHasOwnPolicy = action.Attributes.Any(attribute =>
                attribute is EnableRateLimitingAttribute or DisableRateLimitingAttribute
            );

            if (actionHasOwnPolicy)
                continue;

            bool isRead = action
                .Attributes.OfType<HttpMethodAttribute>()
                .Any(methodAttribute =>
                    methodAttribute.HttpMethods.Any(method =>
                        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
                    )
                );

            if (!isRead)
                continue;

            foreach (SelectorModel selector in action.Selectors)
                selector.EndpointMetadata.Add(
                    new EnableRateLimitingAttribute(RateLimitPolicyNames.Read)
                );
        }
    }
}
