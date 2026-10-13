// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'

import FilterBar from '@/components/layout/FilterBar.vue'

import CatalogueView from '../CatalogueView.vue'
import en from '../i18n/en.json'

vi.mock('@/api/community', () => ({
  useCommunityFirstPage: () => ({
    data: ref([]),
    isError: ref(false),
    isPending: ref(false),
    refetch: async (): Promise<void> => undefined,
  }),
}))
vi.mock('@/api/codeScripts', () => ({
  useCodeScripts: () => ({ data: ref([]), error: ref(null) }),
  fetchScriptProject: async () => null,
  fetchScriptSdkTypes: async () => null,
  saveScriptProject: async () => ({ ok: true }),
}))
vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let pinia: Pinia
let router: Router

beforeEach(() => {
  localStorage.clear()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function open(): Promise<void> {
  const blank = { template: '<div />' }
  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/catalogue', name: 'dashboard-catalogue', component: blank }],
  })
  await router.push('/app/catalogue?view=blocks')
  mounted = mount(CatalogueView, {
    attachTo: document.body,
    global: {
      plugins: [i18n, pinia, router],
      // HelperPanel declares its target prop: an undeclared one falls through as an attribute, and a
      // component instance cannot be written as one.
      stubs: {
        CodeEditor: true,
        ChartPanel: true,
        TemplateField: true,
        HelperPanel: { props: ['target', 'context'], template: '<div />' },
      },
    },
  })
  await flushPromises()
}

// The page has two FilterBars; each one's value is the v-model the page remembers.
const bar = (label: string): VueWrapper =>
  mounted!
    .findAllComponents(FilterBar)
    .find((found) => found.props('label') === label) as VueWrapper

describe('CatalogueView', () => {
  it('keeps the state and since choices after the page is opened again', async () => {
    await open()
    bar(en.dashboard.catalogue.filter.state).vm.$emit('update:modelValue', 'on')
    bar(en.dashboard.catalogue.filter.since).vm.$emit('update:modelValue', 'month')
    await flushPromises()
    mounted!.unmount()
    mounted = null
    document.body.innerHTML = ''

    await open()

    const chosen = (label: string): unknown =>
      (bar(label).props() as { modelValue: unknown }).modelValue
    expect(chosen(en.dashboard.catalogue.filter.state)).toBe('on')
    expect(chosen(en.dashboard.catalogue.filter.since)).toBe('month')
  })
})
