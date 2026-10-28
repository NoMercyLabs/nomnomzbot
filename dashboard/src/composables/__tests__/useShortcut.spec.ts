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
import { defineComponent, h } from 'vue';

import SearchField from '@/components/layout/SearchField.vue';
import { useShortcut, useShortcutList } from '@/composables/useShortcut';
import { i18n } from '@/i18n';
import ShortcutsDialog from '@/layouts/dashboard/ShortcutsDialog.vue';

const mounted: VueWrapper[] = [];

function press(
  key: string,
  options: KeyboardEventInit = {},
  target: EventTarget = document.body,
): void {
  target.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
    ...options,
  }));
}

function withShortcut(mod: boolean) {
  const runs: string[] = [];
  const wrapper = mount(defineComponent({
    setup() {
      useShortcut({
        key: 'e',
        mod,
        label: 'common.shortcuts.search',
      }, () => {
        runs.push('ran');
      });

      return () =>
        h('input', { 'data-field': '' });
    },
  }), { attachTo: document.body });

  mounted.push(wrapper);

  return { runs, wrapper } as const satisfies { runs: string[]; wrapper: VueWrapper };
}

afterEach(() => {
  mounted.splice(0).forEach((wrapper) => {
    wrapper.unmount();
  });

  document.body.innerHTML = '';
});

describe('a keyboard shortcut', () => {
  it('runs on its key and is listed while its part is on screen, and only then', () => {
    const { runs, wrapper } = withShortcut(false);

    press('e');
    expect(runs).toEqual(['ran']);

    expect(useShortcutList().value.map((shortcut) =>
      shortcut.key)).toEqual(['e']);

    wrapper.unmount();
    mounted.splice(0);
    press('e');
    expect(runs).toEqual(['ran']);
    expect(useShortcutList().value).toEqual([]);
  });

  it('leaves a plain letter to the field the person types in', () => {
    const { runs, wrapper } = withShortcut(false);

    press('e', {}, wrapper.find('[data-field]').element);
    expect(runs).toEqual([]);
  });

  it('needs Ctrl or Cmd when it asks for one, and never fires with the wrong modifier', () => {
    const { runs } = withShortcut(true);

    press('e');
    press('e', { altKey: true, ctrlKey: true });
    expect(runs).toEqual([]);

    press('e', { ctrlKey: true });
    press('e', { metaKey: true });
    expect(runs).toEqual(['ran', 'ran']);
  });
});

describe('the shortcut list', () => {
  it('opens on ? and names each shortcut of the page once, with its keys', async () => {
    const page = mount(defineComponent({
      setup() {
        return () =>
          [
            h(SearchField, { placeholder: 'Search chat' }),
            h(SearchField, { placeholder: 'Search people' }),
            h(ShortcutsDialog),
          ];
      },
    }), { attachTo: document.body, global: { plugins: [i18n] } });

    mounted.push(page);
    press('?');
    await flushPromises();

    const dialog = document.body.querySelector<HTMLElement>('[data-shortcuts]');

    expect(dialog).not.toBeNull();

    const labels = [...dialog!.querySelectorAll('[data-shortcut-label]')].map((label) =>
      label.textContent);

    expect(labels).toEqual(['Search this page', 'Show the keyboard shortcuts']);
    expect(dialog!.querySelector('dd')!.textContent!.trim()).toBe('/');
  });

  it('puts the cursor in the search on /', async () => {
    const page = mount(SearchField, {
      attachTo: document.body,
      props: { placeholder: 'Search chat' },
      global: { plugins: [i18n] },
    });

    mounted.push(page);
    press('/');
    await flushPromises();
    expect(document.activeElement).toBe(page.find('input').element);
  });
});
