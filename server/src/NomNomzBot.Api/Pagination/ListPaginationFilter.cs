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

using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace NomNomzBot.Api.Pagination;

/// <summary>
/// Lets the client choose its own page on every list endpoint: <c>?take=</c> (the page size) and <c>?page=</c>
/// (1-based) slice the list the endpoint returns, and <c>X-Total-Count</c>, <c>X-Page</c> and <c>X-Take</c> say
/// how much there is, so a client knows when to stop. A client that sends neither gets the list unchanged.
/// An endpoint that already declares its own paging (a <c>take</c> or <c>page</c> parameter, or a request type
/// with those properties) keeps it: its page is cut in the database, not here.
/// </summary>
public sealed class ListPaginationFilter : IAsyncResultFilter
{
    /// <summary>The largest page a client can ask for; a larger request is clamped to this.</summary>
    public const int MaxTake = 1000;

    public const int DefaultTake = 100;

    public const string TotalHeader = "X-Total-Count";
    public const string PageHeader = "X-Page";
    public const string TakeHeader = "X-Take";

    private static readonly string[] OwnPagingNames =
    [
        "take",
        "page",
        "pageSize",
        "limit",
        "offset",
        "skip",
    ];

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next
    )
    {
        Slice(context);
        await next();
    }

    private static void Slice(ResultExecutingContext context)
    {
        HttpRequest request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method))
            return;

        bool asksTake = request.Query.TryGetValue("take", out StringValues takeText);
        bool asksPage = request.Query.TryGetValue("page", out StringValues pageText);
        if (!asksTake && !asksPage)
            return;

        if (OwnsPaging(context.ActionDescriptor))
            return;

        if (context.Result is not ObjectResult { Value: { } body } result)
            return;

        PropertyInfo? data = body.GetType().GetProperty("Data");
        if (data?.GetValue(body) is not IList list || !data.CanWrite)
            return;

        int take = Math.Clamp(ParsePositive(takeText, DefaultTake), 1, MaxTake);
        int page = ParsePositive(pageText, 1);

        data.SetValue(body, CutPage(data.PropertyType, list, (page - 1) * take, take));

        result.Value = body;
        IHeaderDictionary headers = context.HttpContext.Response.Headers;
        headers[TotalHeader] = list.Count.ToString();
        headers[PageHeader] = page.ToString();
        headers[TakeHeader] = take.ToString();
    }

    private static int ParsePositive(StringValues text, int fallback) =>
        int.TryParse(text.ToString(), out int value) && value > 0 ? value : fallback;

    private static bool OwnsPaging(ActionDescriptor action)
    {
        if (action is not ControllerActionDescriptor controller)
            return false;

        foreach (ParameterDescriptor parameter in controller.Parameters)
        {
            if (OwnPagingNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                return true;

            bool requestCarriesPaging = parameter
                .ParameterType.GetProperties()
                .Any(p => OwnPagingNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase));
            if (requestCarriesPaging && parameter.ParameterType != typeof(string))
                return true;
        }

        return false;
    }

    /// <summary>One page of <paramref name="list"/>, in the collection type the response declared.</summary>
    private static object CutPage(Type declared, IList list, int skip, int take)
    {
        Type element =
            list.GetType().IsArray ? list.GetType().GetElementType()!
            : list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0]
            : typeof(object);

        IList page = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        for (int i = skip; i < Math.Min(list.Count, skip + take); i++)
            page.Add(list[i]);

        if (!declared.IsArray)
            return page;

        Array array = Array.CreateInstance(element, page.Count);
        page.CopyTo(array, 0);

        return array;
    }
}
