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

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { nextTick } from 'vue';

import VirtualList from '@/components/layout/VirtualList.vue';
import { i18n } from '@/i18n';

interface Row {
  id: number;
}

const ROW_HEIGHT = 40;
const VIEWPORT_HEIGHT = 400;

// A row's height now: a narrower list wraps its rows, so a test may make them taller.
let rowHeight = ROW_HEIGHT;

type Observer = { callback: ResizeObserverCallback; targets: Element[] };

const observers: Observer[] = [];

// Every observed element reports a new width, and each row reports the current rowHeight.
function resizeTo(width: number): void {
  for (const observer of observers) {
    const entries = observer.targets.map((target) => {
      const contentRect = { width, height: target.clientHeight };

      return { target, contentRect } as unknown as ResizeObserverEntry;
    });

    observer.callback(entries, {} as ResizeObserver);
  }
}

// jsdom has no layout (TanStack Virtual's own tests mock sizes the same way): the scroll area is
// VIEWPORT_HEIGHT tall and every row is rowHeight tall. Scrolling writes scrollTop and fires a scroll event.
beforeEach(() => {
  rowHeight = ROW_HEIGHT;
  observers.length = 0;

  vi.stubGlobal(
    'ResizeObserver',
    class {
      private readonly observer: Observer;

      constructor(callback: ResizeObserverCallback) {
        this.observer = { callback, targets: [] };
        observers.push(this.observer);
      }

      observe(target: Element): void {
        this.observer.targets.push(target);
      }

      unobserve(target: Element): void {
        this.observer.targets = this.observer.targets.filter((other) =>
          other !== target);
      }

      disconnect(): void {
        this.observer.targets = [];
      }
    },
  );

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? VIEWPORT_HEIGHT
      : rowHeight;

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  // The scrollable height is the height the list sets on its inner column.
  vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return parseInt(
      (this.querySelector('[role="list"]') as HTMLElement | null)?.style.height ?? '0',
      10,
    );
  });

  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(300);

  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 300,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 300,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect;
  });

  HTMLElement.prototype.scrollTo = function (
    this: HTMLElement,
    options?: ScrollToOptions | number,
  ): void {
    this.scrollTop = typeof options === 'object'
      ? (options.top ?? 0)
      : 0;

    this.dispatchEvent(new Event('scroll'));
  } as typeof HTMLElement.prototype.scrollTo;
});

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function rows(count: number): Row[] {
  return Array.from({ length: count }, (_: unknown, id: number) => ({ id }));
}

function list(items: Row[], extra: Record<string, unknown> = {}): VueWrapper {
  return mount(VirtualList, {
    props: {
      items,
      itemKey: (row: unknown) => (row as Row).id,
      estimateSize: ROW_HEIGHT,
      label: 'Rows',
      ...extra,
    },
    slots: {
      default: '<template #default="{ item, index }">row-{{ item.id }}-at-{{ index }}</template>',
    },
    attachTo: document.body,
    global: { plugins: [i18n] },
  });
}

function scroller(wrapper: VueWrapper): HTMLElement {
  return wrapper.find('[data-virtual-scroll]').element as HTMLElement;
}

async function scrollTo(wrapper: VueWrapper, top: number): Promise<void> {
  const element = scroller(wrapper);

  element.scrollTop = top;
  element.dispatchEvent(new Event('scroll'));
  await nextTick();
}

const BOTTOM_OF = (count: number): number => count * ROW_HEIGHT - VIEWPORT_HEIGHT;

describe('the virtual list window', () => {
  it('keeps only a window of 5,000 rows in the DOM, each with its item and index', async () => {
    const wrapper = list(rows(5000));

    await nextTick();

    const shown: number = wrapper.findAll('[role="listitem"]').length;

    expect(shown).toBeGreaterThan(0);
    expect(shown).toBeLessThan(60);
    expect(wrapper.text()).toContain('row-0-at-0');
    expect(wrapper.text()).not.toContain('row-4999-at-4999');
    expect(wrapper.find('[role="list"]').attributes('aria-label')).toBeUndefined();
    expect(wrapper.find('[data-virtual-scroll]').attributes('aria-label')).toBe('Rows');
    wrapper.unmount();
  });

  it('renders the rows that scrolling brings in, keyed by the item and not by position', async () => {
    const wrapper = list(rows(5000));

    await nextTick();
    await scrollTo(wrapper, 2000);

    expect(wrapper.text()).toContain('row-50-at-50');
    expect(wrapper.text()).not.toContain('row-0-at-0');
    expect(wrapper.find('[data-key="50"]').attributes('aria-posinset')).toBe('51');
    expect(wrapper.find('[data-key="50"]').attributes('aria-setsize')).toBe('5000');
    wrapper.unmount();
  });

  it('keeps a row element when an item is added before it', async () => {
    const wrapper = list(rows(10));

    await nextTick();
    const before: Element = wrapper.find('[data-key="3"]').element;

    await wrapper.setProps({ items: [{ id: 99 }, ...rows(10)] });
    await nextTick();

    expect(wrapper.find('[data-key="3"]').element).toBe(before);
    expect(wrapper.find('[data-key="3"]').text()).toBe('row-3-at-4');
    wrapper.unmount();
  });
});

describe('the keyboard', () => {
  it('scrolls by a row with the arrows and by a page with the page keys', async () => {
    const wrapper = list(rows(500));

    await nextTick();
    const area = scroller(wrapper);

    expect(area.getAttribute('tabindex')).toBe('0');

    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'ArrowDown' });
    expect(area.scrollTop).toBe(ROW_HEIGHT);
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'PageDown' });
    expect(area.scrollTop).toBe(ROW_HEIGHT + VIEWPORT_HEIGHT);
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'ArrowUp' });
    expect(area.scrollTop).toBe(VIEWPORT_HEIGHT);
    await wrapper.find('[data-virtual-scroll]').trigger('keydown', { key: 'End' });
    expect(area.scrollTop).toBe(BOTTOM_OF(500));
    wrapper.unmount();
  });
});

// Stoney 2026-10-14: "lazy load all users known to my channel, so i never have to search for someone already
// known to me". The list asks for the next page when its last rows come into view.
describe('the end of the list', () => {
  it('asks for more once the last rows come into view, and again only after more arrive', async () => {
    const wrapper = list(rows(200));

    await nextTick();
    expect(wrapper.emitted('end')).toBeUndefined();

    await scrollTo(wrapper, BOTTOM_OF(200));
    expect(wrapper.emitted('end')).toHaveLength(1);

    await scrollTo(wrapper, BOTTOM_OF(200) - ROW_HEIGHT);
    expect(wrapper.emitted('end')).toHaveLength(1);

    await wrapper.setProps({ items: rows(300) });
    await nextTick();
    expect(wrapper.emitted('end')).toHaveLength(1);

    await scrollTo(wrapper, BOTTOM_OF(300));
    expect(wrapper.emitted('end')).toHaveLength(2);
    wrapper.unmount();
  });

  it('asks for more at once when the first page does not fill the view', async () => {
    const wrapper = list(rows(3));

    await nextTick();
    await nextTick();

    expect(wrapper.emitted('end')).toHaveLength(1);
    wrapper.unmount();
  });
});

describe('the stick-to-bottom mode', () => {
  it('starts at the bottom and stays there when a row arrives', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true });

    await nextTick();
    await nextTick();
    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(50));

    await wrapper.setProps({ items: rows(51) });
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(51));
    expect(wrapper.text()).toContain('row-50-at-50');
    expect(wrapper.find('[data-new-below]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('keeps the place of a reader who scrolled up, counts the new rows and jumps back on request', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true });

    await nextTick();
    await nextTick();
    await scrollTo(wrapper, 400);

    await wrapper.setProps({ items: rows(52) });
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(400);
    const jump = wrapper.find('[data-new-below]');

    expect(jump.text()).toBe('2 new messages below');

    await wrapper.setProps({ items: rows(53) });
    await nextTick();
    expect(wrapper.find('[data-new-below]').text()).toBe('3 new messages below');

    await wrapper.find('[data-new-below]').trigger('click');
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(53));
    expect(wrapper.find('[data-new-below]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('clears the count when the reader scrolls to the bottom by hand', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true });

    await nextTick();
    await nextTick();
    await scrollTo(wrapper, 400);
    await wrapper.setProps({ items: rows(51) });
    await nextTick();
    expect(wrapper.find('[data-new-below]').exists()).toBe(true);

    await scrollTo(wrapper, BOTTOM_OF(51));

    expect(wrapper.find('[data-new-below]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('holds the view while the pointer is over the feed and jumps to the newest on leave', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true, pauseOnHover: true });

    await nextTick();
    await nextTick();
    await scroller(wrapper).dispatchEvent(new Event('pointerenter'));

    await wrapper.setProps({ items: rows(52) });
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(50));
    expect(wrapper.find('[data-new-below]').text()).toBe('2 new messages below');

    await scroller(wrapper).dispatchEvent(new Event('pointerleave'));
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(52));
    expect(wrapper.find('[data-new-below]').exists()).toBe(false);
    wrapper.unmount();
  });

  it('keeps following new rows under the pointer when pauseOnHover is off', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true });

    await nextTick();
    await nextTick();
    await scroller(wrapper).dispatchEvent(new Event('pointerenter'));
    await wrapper.setProps({ items: rows(51) });
    await nextTick();
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(BOTTOM_OF(51));
    wrapper.unmount();
  });

  it('does not count or move when the list is not in stick mode', async () => {
    const wrapper = list(rows(50));

    await nextTick();
    await scrollTo(wrapper, 400);

    await wrapper.setProps({ items: rows(52) });
    await nextTick();

    expect(scroller(wrapper).scrollTop).toBe(400);
    expect(wrapper.find('[data-new-below]').exists()).toBe(false);
    wrapper.unmount();
  });
});

// Owner 2026-10-14: "chat layout shifts when selecting a user". A side panel narrows the list, the rows
// rewrap and grow taller, and the reader's place must hold. "it still bounces down and up": the place
// holds in the same measure step, so the checks wait for no animation frame.
describe('a list that changes width', () => {
  function rowTop(wrapper: VueWrapper, index: number): number {
    const row = wrapper.find(`[data-index="${index}"]`).element as HTMLElement;
    const start = Number(/translateY\((\d+)px\)/.exec(row.style.transform)![1]);

    return start - scroller(wrapper).scrollTop;
  }

  it('keeps a reader at the bottom at the bottom when the rows grow taller', async () => {
    const wrapper: VueWrapper = list(rows(50), { stick: true });

    await nextTick();
    await nextTick();
    resizeTo(300);
    rowHeight = 80;
    resizeTo(200);
    await nextTick();

    const total = parseInt((wrapper.find('[role="list"]').element as HTMLElement).style.height, 10);

    expect(scroller(wrapper).scrollTop).toBe(total - VIEWPORT_HEIGHT);
    wrapper.unmount();
  });

  it('keeps the middle row of a reader who scrolled up in the same place', async () => {
    const wrapper: VueWrapper = list(rows(100), { stick: true });

    await nextTick();
    await nextTick();
    resizeTo(300);
    await scrollTo(wrapper, 1000);

    const before = rowTop(wrapper, 30);

    rowHeight = 80;
    resizeTo(200);
    await nextTick();

    expect(before).toBe(200);
    expect(rowTop(wrapper, 30)).toBe(before);
    wrapper.unmount();
  });
});
