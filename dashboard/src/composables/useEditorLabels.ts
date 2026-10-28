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

import type { JsonObject } from '@/lib/json';
import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';

// The words of the served editor page in the dashboard's language. The page names each text by a camelCase
// id (DEFAULT_LABELS in editor.js) and fills its own `{name}` placeholders, so each one is handed back as
// itself: `{path}` stays `{path}` in the text that goes to the page.
export function useEditorLabels(
  withTestRun: MaybeRefOrGetter<boolean>,
): ComputedRef<JsonObject> {
  const { t, tm, locale } = useI18n();

  function words(group: string): Record<string, string> {
    const out: Record<string, string> = {};

    for (const [id, raw] of Object.entries(tm(group) as JsonObject)) {
      if (typeof raw !== 'string') continue;

      const own: Record<string, string> = {};

      for (const match of raw.matchAll(/\{(\w+)\}/g)) {
        own[match[1]!] = match[0];
      }

      out[id] = t(`${group}.${id}`, own);
    }

    return out;
  }

  return computed(() => {
    void locale.value;
    const labels: JsonObject = words('codeEditor.labels');

    if (!toValue(withTestRun))
      return labels;

    return {
      ...labels,
      ...words('codeEditor.testRun'),
      roles: words('codeEditor.testRun.roles'),
    };
  });
}
