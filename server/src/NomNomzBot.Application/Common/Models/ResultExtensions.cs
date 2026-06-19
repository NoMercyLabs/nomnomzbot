// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

public static class ResultExtensions
{
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> mapper) =>
        result.IsSuccess
            ? Result<TOut>.Success(mapper(result.Value))
            : Result<TOut>.Failure(result.ErrorMessage!, result.ErrorCode, result.ErrorDetail);

    public static Result<TOut> Bind<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Result<TOut>> binder
    ) =>
        result.IsSuccess
            ? binder(result.Value)
            : Result<TOut>.Failure(result.ErrorMessage!, result.ErrorCode, result.ErrorDetail);

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<Result<TOut>>> binder
    ) =>
        result.IsSuccess
            ? await binder(result.Value)
            : Result<TOut>.Failure(result.ErrorMessage!, result.ErrorCode, result.ErrorDetail);

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> binder
    )
    {
        Result<TIn> result = await resultTask;
        return result.IsSuccess
            ? await binder(result.Value)
            : Result<TOut>.Failure(result.ErrorMessage!, result.ErrorCode, result.ErrorDetail);
    }

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<string, string?, TOut> onFailure
    ) =>
        result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.ErrorMessage!, result.ErrorCode);

    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess)
            action(result.Value);
        return result;
    }

    public static async Task<Result<T>> TapAsync<T>(this Result<T> result, Func<T, Task> action)
    {
        if (result.IsSuccess)
            await action(result.Value);
        return result;
    }

    public static Result<T> ToTyped<T>(this Result result) =>
        result.IsSuccess
            ? throw new InvalidOperationException(
                "Cannot convert a successful void Result to a typed Result without a value."
            )
            : Result<T>.Failure(result.ErrorMessage!, result.ErrorCode, result.ErrorDetail);
}
