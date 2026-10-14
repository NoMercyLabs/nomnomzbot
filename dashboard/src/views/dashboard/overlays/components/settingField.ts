// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useI18n } from 'vue-i18n';

import type { components } from '@/api/schema';
import type { WidgetSettingsField } from '@/api/widgets';

type LocalizedText = components['schemas']['LocalizedText'];

export type SettingText = (text: LocalizedText | null | undefined, fallback: string) => string;

// A label in this order: the text the bot sent, the translation of its key, then the fallback.
export function useSettingText(): SettingText {
  const { t, te } = useI18n();

  return (text: LocalizedText | null | undefined, fallback: string): string => {
    if (text?.text)
      return text.text;

    if (text?.key && te(text.key))
      return t(text.key);

    return fallback;
  };
}

// A readable name from a settings key when no label text exists: "fontSize" -> "Font size".
export function humanize(key: string): string {
  const words = key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_.-]+/g, ' ')
    .trim()
    .toLowerCase();

  return words.charAt(0).toUpperCase() + words.slice(1);
}

// Settings values are plain JSON, so two values are the same when they serialize the same.
export function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

export function numberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || value === '')
    return null;

  const parsed = Number(value);

  return Number.isFinite(parsed)
    ? parsed
    : null;
}

// Holds a typed number inside the field's min and max; text that is not a number keeps the old value.
export function clampNumber(typed: string, field: WidgetSettingsField, fallback: unknown): unknown {
  const parsed = numberOrNull(typed);

  if (parsed === null)
    return fallback;

  const min = numberOrNull(field.min);
  const max = numberOrNull(field.max);

  if (min !== null && parsed < min)
    return min;

  if (max !== null && parsed > max)
    return max;

  return parsed;
}
