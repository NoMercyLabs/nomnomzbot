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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { computed, defineComponent, h, ref } from 'vue';

import ListPanel from '@/components/layout/ListPanel.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

const NAMES = ['!so', '!hug', '!lurk'] as const;

// A page the way the six list pages use it: the search model filters the rows the slot shows.
const Page = defineComponent({
  props: { headless: Boolean },
  setup: (props) => {
    const search = ref('');
    const rows = computed(() => NAMES.filter((name) => name.includes(search.value.trim())));

    return () => h(
      ListPanel,
      {
        modelValue: search.value,
        'onUpdate:modelValue': (value: string) => {
          search.value = value;
        },
        title: 'Commands',
        placeholder: 'Search commands',
        headless: props.headless,
      },
      {
        default: () => rows.value.map((name) => h('p', { 'data-row': '' }, name)),
        actions: () => h('button', { 'data-extra': '' }, 'Sort'),
      },
    );
  },
});

function rows(): string[] {
  return Array.from(document.body.querySelectorAll('[data-row]')).map((row) => row.textContent ?? '');
}

describe('the list panel', () => {
  it('puts the search in the panel head, right of the extra actions, with the concentric radius', () => {
    mounted = mount(Page, { attachTo: document.body, global: { plugins: [i18n] } });

    const actions = mounted.get('[data-slot="panel-actions"]').element;
    const children = Array.from(actions.children);

    expect(children[0]!.getAttribute('data-extra')).toBe('');
    expect(children[1]!.getAttribute('data-slot')).toBe('search-field');
    expect(children[1]!.className).toContain('-mr-2');
    expect(children[1]!.className).toContain('rounded-[12px]');
  });

  it('filters the rows of the slot as the person types', async () => {
    mounted = mount(Page, { attachTo: document.body, global: { plugins: [i18n] } });

    expect(rows()).toEqual(['!so', '!hug', '!lurk']);

    await mounted.get('input[type="search"]').setValue('hu');
    await flushPromises();

    expect(rows()).toEqual(['!hug']);
  });

  it('focuses the search on the key "/"', () => {
    mounted = mount(Page, { attachTo: document.body, global: { plugins: [i18n] } });

    document.body.dispatchEvent(new KeyboardEvent('keydown', {
      key: '/',
      bubbles: true,
      cancelable: true,
    }));

    expect(document.activeElement).toBe(mounted.get('input[type="search"]').element);
  });

  it('shows no head, so no search box, when the page asks for a headless list', () => {
    mounted = mount(Page, {
      props: { headless: true },
      attachTo: document.body,
      global: { plugins: [i18n] },
    });

    expect(mounted.find('[data-slot="search-field"]').exists()).toBe(false);
    expect(rows()).toEqual(['!so', '!hug', '!lurk']);
  });
});
