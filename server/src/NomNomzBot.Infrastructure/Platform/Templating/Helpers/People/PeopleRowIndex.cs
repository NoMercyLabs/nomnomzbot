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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The viewer rows one reader answers, found by key. A reader names the sides it wants: the rows of the first
/// side win, and a row of the fallback side is added only for a suffix the first side lacks. The <c>user</c> and
/// <c>target</c> providers read one side each; <c>args.&lt;n&gt;.user.*</c> reads the user rows first and the
/// target rows after them, because a person typed after a command can be asked anything about either side.
/// </summary>
internal sealed class PeopleRowIndex
{
    private readonly Dictionary<string, PeopleRow> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PeopleRow> _prefixes = [];
    private readonly List<PeopleRow> _rows = [];

    /// <param name="first">The side whose rows win.</param>
    /// <param name="fallback">A second side whose rows fill the suffixes the first lacks; 0 for none.</param>
    /// <param name="extra">Rows only this reader answers; they too yield to a row of the groups.</param>
    public PeopleRowIndex(
        ViewerSides first,
        ViewerSides fallback = 0,
        IReadOnlyList<PeopleRow>? extra = null
    )
    {
        List<PeopleRow> all = [.. PeopleGroups.All().SelectMany(g => g.Rows)];
        foreach (PeopleRow row in all.Where(r => r.Sides.HasFlag(first)))
            Add(row);
        if (fallback != 0)
        {
            foreach (PeopleRow row in all.Where(r => r.Sides.HasFlag(fallback) && !Has(r.Suffix)))
                Add(row);
        }
        foreach (PeopleRow row in extra ?? [])
        {
            if (!Has(row.Suffix))
                Add(row);
        }
    }

    /// <summary>Every row of the index, in the order the groups list them.</summary>
    public IReadOnlyList<PeopleRow> Rows => _rows;

    /// <summary>The row that owns the key after the namespace (<c>points.rank</c>), with the rest of a prefix key.</summary>
    public (PeopleRow? Row, string Tail) Find(string suffix)
    {
        if (_exact.TryGetValue(suffix, out PeopleRow? exact))
            return (exact, string.Empty);

        foreach (PeopleRow row in _prefixes)
        {
            if (
                suffix.Length > row.Suffix.Length
                && suffix.StartsWith(row.Suffix, StringComparison.OrdinalIgnoreCase)
            )
                return (row, suffix[row.Suffix.Length..]);
        }
        return (null, string.Empty);
    }

    private bool Has(string suffix) =>
        _exact.ContainsKey(suffix)
        || _prefixes.Exists(p =>
            string.Equals(p.Suffix, suffix, StringComparison.OrdinalIgnoreCase)
        );

    private void Add(PeopleRow row)
    {
        _rows.Add(row);
        if (row.IsPrefix)
            _prefixes.Add(row);
        else
            _exact.Add(row.Suffix, row);
    }
}
