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

import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import CatalogCodeEditor from '../components/CatalogCodeEditor.vue';

vi.mock('@/api/codeScripts', () => ({
  useCodeScripts: () => ({
    data: ref([]),
    error: ref(new Error('Scripts could not load')),
  }),
  fetchScriptProject: async () => null,
  fetchScriptSdkTypes: async () => null,
  saveScriptProject: async () => ({ ok: true }),
}));

describe('CatalogCodeEditor', () => {
  it('shows a failed script load through the error notice beside the open button', () => {
    const wrapper = mount(CatalogCodeEditor, {
      global: {
        plugins: [i18n],
        stubs: { CodeEditor: true },
      },
    });
    const notice = wrapper.get('[data-tone="error"]');

    expect(notice.text()).toBe('Scripts could not load');
    expect(notice.attributes('role')).toBe('alert');
  });
});
