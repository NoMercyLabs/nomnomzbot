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

// A widget definition's payload is `{ "sourceCode", "defaultSettings", "defaultEventSubscriptions" }`: the one Vue
// source a channel copy starts from, the settings it starts with and the events it listens to. The widget form
// edits the source and the settings in the code editor (two files) and the events as a list, so reading and
// writing change no part of the payload.
import type { JsonObject, JsonValue } from '@/lib/json';
import { objectOf, textListOf } from '@/views/dashboard/overlays/components/settingField';

// The file names the code editor shows: the source as the server names a one-file Vue widget, and the settings.
export const WIDGET_ENTRY = 'index.vue';
export const WIDGET_SETTINGS = 'settings.json';

// The words of the list editor belong to the overlays page; this form loads them itself (ensureWords).
export const overlayWords = (locale: string) => import(`../../overlays/i18n/${locale}.json`);

const KEYS: readonly string[] = [
  'sourceCode',
  'defaultSettings',
  'defaultEventSubscriptions',
] as const;

export type WidgetPayload = {
  sourceCode: string;
  defaultSettings: JsonObject;
  defaultEventSubscriptions: string[];
};

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

// The parts of the payload, or null when the payload holds anything the widget form would drop: another field,
// a source that is not text, settings that are not an object, events that are not a list of text, or text that
// is not JSON. The caller then shows the raw JSON. A part the payload leaves out reads as empty, the way the
// server reads it.
export function readWidgetPayload(payloadJson: string): WidgetPayload | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((name) => KEYS.includes(name)))
    return null;

  const sourceCode = parsed.sourceCode;
  const defaultSettings = objectOf(parsed.defaultSettings ?? {});
  const defaultEventSubscriptions = textListOf(parsed.defaultEventSubscriptions ?? []);

  if (typeof sourceCode !== 'string' || defaultSettings === null || defaultEventSubscriptions === null)
    return null;

  return {
    sourceCode,
    defaultSettings,
    defaultEventSubscriptions,
  };
}

export function writeWidgetPayload(payload: WidgetPayload): string {
  return JSON.stringify(payload);
}

// The settings as the code editor's second file.
export function settingsText(settings: JsonObject): string {
  return JSON.stringify(settings, null, 2);
}

// The settings the editor's file holds, or null when the text is not a JSON object.
export function readSettingsText(text: string): JsonObject | null {
  return objectOf(parseJson(text));
}
