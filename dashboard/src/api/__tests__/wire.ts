// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A test answer as the server sends it. The API leaves out every null field (Program.cs, WhenWritingNull),
// so a fixture with explicit nulls hides a read that breaks live: the field is missing, not null.
export function onTheWire<T>(body: T): T {
  return JSON.parse(JSON.stringify(body, (_key: string, value: unknown) =>
    value === null
      ? undefined
      : value)) as T;
}
