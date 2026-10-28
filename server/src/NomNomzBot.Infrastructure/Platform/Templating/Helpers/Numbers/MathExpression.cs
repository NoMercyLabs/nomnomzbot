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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// The safe evaluator behind <c>{math:...}</c>: numbers, <c>+ - * / % ^</c>, brackets and the functions
/// <c>sqrt abs floor ceil pow log</c>. A hand-written recursive-descent parser, so there is no eval, no Roslyn
/// and no reflection; the text length and the nesting depth are bounded. Any text that is not a complete,
/// valid expression gives null.
///
/// Precedence from low to high: <c>+ -</c>, then <c>* / %</c>, then unary <c>-</c>, then <c>^</c> (right to left),
/// so <c>-2^2</c> is -4 and <c>2^3^2</c> is 512.
/// </summary>
internal static class MathExpression
{
    public const int MaxLength = 200;
    public const int MaxDepth = 32;

    public static decimal? Evaluate(string expression)
    {
        if (expression.Length == 0 || expression.Length > MaxLength)
            return null;

        Parser parser = new(expression);
        decimal? value = parser.ParseExpression();
        return parser.AtEnd ? value : null;
    }

    private sealed class Parser(string text)
    {
        private int _position;
        private int _depth;

        public bool AtEnd
        {
            get
            {
                SkipSpaces();
                return _position >= text.Length;
            }
        }

        public decimal? ParseExpression()
        {
            if (!Enter())
                return null;

            decimal? left = ParseTerm();
            while (left is not null && TryTake("+-", out char op))
            {
                decimal? right = ParseTerm();
                left = right is null ? null : Apply(op, left.Value, right.Value);
            }

            _depth--;
            return left;
        }

        private decimal? ParseTerm()
        {
            decimal? left = ParseUnary();
            while (left is not null && TryTake("*/%", out char op))
            {
                decimal? right = ParseUnary();
                left = right is null ? null : Apply(op, left.Value, right.Value);
            }
            return left;
        }

        private static decimal? Apply(char op, decimal left, decimal right) =>
            op switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' => NumberMath.Divide(left, right),
                _ => NumberMath.Modulo(left, right),
            };

        private decimal? ParseUnary()
        {
            if (!Enter())
                return null;

            decimal? value;
            if (TryTake("-", out _))
                value = -ParseUnary();
            else if (TryTake("+", out _))
                value = ParseUnary();
            else
                value = ParsePower();

            _depth--;
            return value;
        }

        private decimal? ParsePower()
        {
            decimal? value = ParsePrimary();
            if (value is null || !TryTake("^", out _))
                return value;

            decimal? exponent = ParseUnary();
            return exponent is null ? null : NumberMath.Pow(value.Value, exponent.Value);
        }

        private decimal? ParsePrimary()
        {
            SkipSpaces();
            if (_position >= text.Length)
                return null;

            char c = text[_position];
            if (char.IsAsciiDigit(c) || c == '.')
                return ParseNumber();
            if (c == '(')
                return ParseGroup();
            return char.IsAsciiLetter(c) ? ParseCall() : null;
        }

        private decimal? ParseNumber()
        {
            int start = _position;
            while (
                _position < text.Length
                && (char.IsAsciiDigit(text[_position]) || text[_position] == '.')
            )
                _position++;

            return NumberArguments.TryNumber(text[start.._position], out decimal value)
                ? value
                : null;
        }

        private decimal? ParseGroup()
        {
            _position++;
            decimal? inner = ParseExpression();
            return inner is not null && TryTake(")", out _) ? inner : null;
        }

        private decimal? ParseCall()
        {
            int start = _position;
            while (_position < text.Length && char.IsAsciiLetter(text[_position]))
                _position++;
            string name = text[start.._position].ToLowerInvariant();

            if (!TryTake("(", out _))
                return null;

            List<decimal>? arguments = ParseArguments();
            return arguments is null ? null : CallFunction(name, arguments);
        }

        private List<decimal>? ParseArguments()
        {
            List<decimal> arguments = [];
            do
            {
                decimal? argument = ParseExpression();
                if (argument is null)
                    return null;
                arguments.Add(argument.Value);
            } while (TryTake(",", out _));

            return TryTake(")", out _) ? arguments : null;
        }

        private static decimal? CallFunction(string name, List<decimal> args) =>
            (name, args.Count) switch
            {
                ("sqrt", 1) => NumberMath.Sqrt(args[0]),
                ("abs", 1) => Math.Abs(args[0]),
                ("floor", 1) => Math.Floor(args[0]),
                ("ceil", 1) => Math.Ceiling(args[0]),
                ("pow", 2) => NumberMath.Pow(args[0], args[1]),
                ("log", 1) => NumberMath.Log(args[0], null),
                ("log", 2) => NumberMath.Log(args[0], args[1]),
                _ => null,
            };

        private bool Enter() => ++_depth <= MaxDepth;

        private bool TryTake(string allowed, out char taken)
        {
            SkipSpaces();
            taken = _position < text.Length ? text[_position] : '\0';
            if (taken == '\0' || !allowed.Contains(taken))
                return false;
            _position++;
            return true;
        }

        private void SkipSpaces()
        {
            while (_position < text.Length && text[_position] == ' ')
                _position++;
        }
    }
}
