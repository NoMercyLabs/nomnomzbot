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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import IconAction from '@/components/layout/IconAction.vue';

let mounted: VueWrapper | undefined;

beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  mounted?.unmount();
  document.body.innerHTML = '';
});

function mountAction(onClick: () => void): VueWrapper {
  return mount(IconAction, {
    props: { label: 'Delete Home server' },
    attrs: { 'data-probe': '', onClick },
    slots: { default: '<i data-icon />' },
    attachTo: document.body,
  });
}

describe('IconAction', () => {
  it('names itself to a screen reader and keeps its data attributes and click on the button', async () => {
    let clicks = 0;

    mounted = mountAction(() => {
      clicks += 1;
    });

    const button = mounted.get('button');

    expect(button.attributes('aria-label')).toBe('Delete Home server');
    expect(button.attributes('data-probe')).toBeDefined();
    expect(button.find('[data-icon]').exists()).toBe(true);

    await button.trigger('click');

    expect(clicks).toBe(1);
  });

  it('shows its label as a tooltip when the button takes keyboard focus', async () => {
    mounted = mountAction(() => undefined);

    await mounted.get('button').trigger('focus');
    await flushPromises();

    expect(document.body.querySelector('[data-slot="tooltip-content"]')?.textContent).toContain(
      'Delete Home server',
    );
  });
});
