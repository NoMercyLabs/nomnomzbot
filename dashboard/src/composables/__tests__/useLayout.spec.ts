// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, useTemplateRef } from 'vue';

import { setWidth } from '@/__tests__/viewport';
import { useFits, useNarrowScreen, usePhone, useScreenFrom, widths } from '@/composables/useLayout';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  vi.unstubAllGlobals();
});

// Stoney 2026-10-18: "extract things like these into reusables". Every "is there room?" answer comes from this table.
describe('the width table', () => {
  it('holds the widths the frame, the panels and the pages switch at', () => {
    expect(widths).toEqual({
      rail: 600,
      tablet: 768,
      sidebar: 840,
      besideRail: 1080,
      wide: 1280,
      twoPanels: 1800,
    });
  });
});

function probe<T>(read: () => T): { value: () => T } {
  let result: T | undefined;

  mounted = mount(defineComponent({
    setup() {
      result = read();

      return () => h('div');
    },
  }));

  return { value: () => result as T };
}

describe('a window width check', () => {
  it('flips exactly at the named width', () => {
    setWidth(839);
    expect(probe(() => useScreenFrom('sidebar').value).value()).toBe(false);
    mounted?.unmount();

    setWidth(840);
    expect(probe(() => useScreenFrom('sidebar').value).value()).toBe(true);
  });
});

describe('the phone checks', () => {
  function stub(width: number, touch: boolean): void {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query.includes('pointer: coarse')
        ? touch
        : width <= 767.9,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }));
  }

  it('calls a narrow window narrow, touch or not', () => {
    stub(500, false);
    expect(probe(() => useNarrowScreen().value).value()).toBe(true);
  });

  it('calls only a narrow touch screen a phone', () => {
    stub(500, false);
    expect(probe(() => usePhone().value).value()).toBe(false);
    mounted?.unmount();

    stub(500, true);
    expect(probe(() => usePhone().value).value()).toBe(true);
    mounted?.unmount();

    stub(1200, true);
    expect(probe(() => usePhone().value).value()).toBe(false);
  });
});

describe('useFits', () => {
  let resize: (width: number) => void = () => {};

  function stubObserver(): void {
    vi.stubGlobal('ResizeObserver', class {
      constructor(callback: ResizeObserverCallback) {
        resize = (width: number): void => {
          callback([{
            contentRect: { width, height: 0 },
            contentBoxSize: [{ inlineSize: width, blockSize: 0 }],
            target: document.body,
          } as unknown as ResizeObserverEntry], this as unknown as ResizeObserver);
        };
      }

      observe(): void {}

      unobserve(): void {}

      disconnect(): void {}
    });
  }

  async function mountMeasured(): Promise<{ width: () => number; fits: () => boolean }> {
    let state: ReturnType<typeof useFits> | undefined;

    mounted = mount(defineComponent({
      setup() {
        const box = useTemplateRef<HTMLElement>('box');

        state = useFits(box, 'wide');

        return () => h('div', { ref: 'box' });
      },
    }));

    await nextTick();

    return { width: () => state!.width.value, fits: () => state!.fits.value };
  }

  it('starts from the window width, the way a board starts before it has measured', async () => {
    stubObserver();
    setWidth(1500);

    const measured = await mountMeasured();

    expect(measured.width()).toBe(1500);
    expect(measured.fits()).toBe(true);
  });

  it('flips at the threshold when the element resizes', async () => {
    stubObserver();
    setWidth(1500);

    const measured = await mountMeasured();

    resize(widths.wide - 1);
    await nextTick();
    expect(measured.width()).toBe(widths.wide - 1);
    expect(measured.fits()).toBe(false);

    resize(widths.wide);
    await nextTick();
    expect(measured.fits()).toBe(true);
  });
});
