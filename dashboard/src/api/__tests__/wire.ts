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

// A test answer as the server sends it. The API leaves out every null field (Program.cs, WhenWritingNull),
// so a fixture with explicit nulls hides a read that breaks live: the field is missing, not null.
export function onTheWire<T>(body: T): T {
  return JSON.parse(JSON.stringify(body, (_key: string, value: unknown) =>
    value === null
      ? undefined
      : value)) as T;
}
