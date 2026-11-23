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

// A code script definition's payload is `{ "sourceCode": "..." }`: the one TypeScript source the server compiles
// and writes as a new version onto every channel copy. The script code editor edits that source as its one file,
// so reading and writing change no part of the payload.
import type { JsonValue } from '@/lib/json';

// The file name the server gives a one-file script project; the channel editor shows the same name.
export const SCRIPT_ENTRY = 'index.ts';

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

// The source of the payload, or null when the payload holds anything the code editor would drop: another field,
// a source that is not text, or text that is not JSON. The caller then shows the raw JSON.
export function readScriptPayload(payloadJson: string): string | null {
  const parsed = parseJson(payloadJson);

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed))
    return null;

  const names = Object.keys(parsed);
  const source = parsed.sourceCode;

  if (names.length !== 1 || names[0] !== 'sourceCode' || typeof source !== 'string')
    return null;

  return source;
}

export function writeScriptPayload(sourceCode: string): string {
  return JSON.stringify({ sourceCode });
}
