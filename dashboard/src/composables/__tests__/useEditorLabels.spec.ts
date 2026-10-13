// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'
import { createApp, defineComponent, h, type ComputedRef } from 'vue'
import { createI18n } from 'vue-i18n'

import en from '@/i18n/locales/en.json'
import nl from '@/i18n/locales/nl.json'

import { useEditorLabels } from '../useEditorLabels'

// The label ids the served editor page declares (DEFAULT_LABELS in editor.js), read from the real file.
function pageLabelIds(): string[] {
  const script: string = readFileSync(
    resolve(__dirname, '../../../../server/src/NomNomzBot.Api/Assets/editor/editor.js'),
    'utf8',
  )
  const start = script.indexOf('const DEFAULT_LABELS = Object.freeze({')
  const body = script.slice(start, script.indexOf('});', start))

  return [...body.matchAll(/^ {4}(\w+):/gm)].map((match) => match[1]!)
}

function labelsFor(locale: 'en' | 'nl', withTestRun: boolean): Record<string, unknown> {
  let labels: ComputedRef<Record<string, unknown>> | undefined
  const i18n = createI18n({ legacy: false, locale, messages: { en, nl } })
  const Probe = defineComponent({
    setup() {
      labels = useEditorLabels(() => withTestRun)

      return () => h('div')
    },
  })

  createApp(Probe).use(i18n).mount(document.createElement('div'))

  return labels!.value
}

describe('useEditorLabels', () => {
  it('has a non-empty word in English and Dutch for every label the page declares', () => {
    const ids = pageLabelIds()

    expect(ids.length).toBeGreaterThan(50)

    for (const locale of ['en', 'nl'] as const) {
      const labels = labelsFor(locale, false)

      expect(ids.filter((id) => typeof labels[id] !== 'string' || labels[id] === '')).toEqual([])
    }
  })

  it('hands the placeholders back to the page untouched', () => {
    const labels: Record<string, unknown> = labelsFor('nl', false)

    expect(labels.fileExists).toBe('{path} bestaat al.')
    expect(labels.cursorPosition).toBe('Rg {line}, Kol {column}')
  })

  it('adds the test run words only when a test run is wired', () => {
    expect(labelsFor('en', false).roles).toBeUndefined()
    const labels: Record<string, unknown> = labelsFor('en', true)

    expect(labels.manual).toBe('Manual')
    expect(labels.roles).toMatchObject({ broadcaster: 'Broadcaster', viewer: 'Viewer' })
  })
})
