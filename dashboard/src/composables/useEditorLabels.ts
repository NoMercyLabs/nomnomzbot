// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue'
import { useI18n } from 'vue-i18n'

// The words of the served editor page in the dashboard's language. The page names each text by a camelCase
// id (DEFAULT_LABELS in editor.js) and fills its own `{name}` placeholders, so each one is handed back as
// itself: `{path}` stays `{path}` in the text that goes to the page.
export function useEditorLabels(
  withTestRun: MaybeRefOrGetter<boolean>,
): ComputedRef<Record<string, unknown>> {
  const { t, tm, locale } = useI18n()

  function words(group: string): Record<string, string> {
    const out: Record<string, string> = {}

    for (const [id, raw] of Object.entries(tm(group) as Record<string, unknown>)) {
      if (typeof raw !== 'string') continue

      const own: Record<string, string> = {}

      for (const match of raw.matchAll(/\{(\w+)\}/g)) own[match[1]!] = match[0]

      out[id] = t(`${group}.${id}`, own)
    }

    return out
  }

  return computed(() => {
    void locale.value
    const labels: Record<string, unknown> = words('codeEditor.labels')

    if (!toValue(withTestRun))
      return labels

    return { ...labels, ...words('codeEditor.testRun'), roles: words('codeEditor.testRun.roles') }
  })
}
