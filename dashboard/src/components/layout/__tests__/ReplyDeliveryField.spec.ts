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

import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';

import ReplyDeliveryField from '@/components/layout/ReplyDeliveryField.vue';
import {
  deliveryOf,
  deliveryToSave,
  lookOf,
  plainDelivery,
  sameDelivery,
  withLook,
  type ReplyDelivery,
} from '@/components/layout/replyMode';
import { i18n } from '@/i18n';

function field(modelValue: ReplyDelivery, disabled: boolean = false): VueWrapper {
  return mount(ReplyDeliveryField, {
    attachTo: document.body,
    props: {
      modelValue,
      disabled,
    },
    global: { plugins: [i18n] },
  });
}

afterEach(() => {
  document.body.innerHTML = '';
});

function durationInput(wrapper: VueWrapper): DOMWrapper<HTMLInputElement> {
  return wrapper.find<HTMLInputElement>('input[inputmode="numeric"]');
}

async function pickHighlight(label: string): Promise<void> {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;

  document.body.querySelector<HTMLElement>('[data-reply-look-highlight]')!.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await flushPromises();

  const option = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]')).find(
    (node) => node.textContent?.trim() === label,
  )!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
  await flushPromises();
}

function segments(wrapper: VueWrapper): DOMWrapper<Element>[] {
  return wrapper.findAll('[data-slot="toggle-group-item"]');
}

function chosen(wrapper: VueWrapper): string {
  return segments(wrapper)
    .filter((segment) => segment.attributes('data-state') === 'on')
    .map((segment) => segment.text())
    .join(',');
}

function segment(wrapper: VueWrapper, label: string): DOMWrapper<Element> {
  return segments(wrapper).find((item) => item.text() === label)!;
}

function lastUpdate(wrapper: VueWrapper): ReplyDelivery | undefined {
  const updates = wrapper.emitted<[ReplyDelivery]>('update:modelValue') ?? [];

  return updates.at(-1)?.[0];
}

describe('the reply delivery field', () => {
  it('names the four places a reply can go and shows Twitch chat for a command that never set one', () => {
    const wrapper = field(deliveryOf(null));

    expect(segments(wrapper).map((item) => item.text())).toEqual([
      'Twitch chat',
      'Chat overlay',
      'Both',
      'Nowhere',
    ]);

    expect(chosen(wrapper)).toBe('Twitch chat');
  });

  it('emits the overlay target and keeps HTML off until the person switches it on', async () => {
    const wrapper = field(plainDelivery);

    await segment(wrapper, 'Chat overlay').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({ target: 'overlay', html: false });
  });

  it('emits the exact delivery when HTML is switched on for the overlay', async () => {
    const wrapper = field({ target: 'overlay', html: false });

    await wrapper.find('button[role="switch"]').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({ target: 'overlay', html: true });
  });

  it('cannot switch HTML on while the reply goes to Twitch chat or nowhere, and drops it on the way there', async () => {
    const chat = field({ target: 'chat', html: false });

    expect(chat.find('button[role="switch"]').attributes('disabled')).toBeDefined();

    const wrapper = field({ target: 'both', html: true });

    await segment(wrapper, 'Nowhere').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({ target: 'none', html: false });
  });

  it('locks every control while the editor is locked', () => {
    const wrapper = field(plainDelivery, true);

    expect(segments(wrapper).every((item) => item.attributes('disabled') !== undefined)).toBe(true);
    expect(wrapper.find('button[role="switch"]').attributes('disabled')).toBeDefined();
  });
});

describe('the reply look section', () => {
  it('shows only when the reply reaches the overlay', () => {
    expect(field({ target: 'chat', html: false }).find('[data-reply-look]').exists()).toBe(false);
    expect(field({ target: 'none', html: false }).find('[data-reply-look]').exists()).toBe(false);
    expect(field({ target: 'overlay', html: false }).find('[data-reply-look]').exists()).toBe(true);
    expect(field({ target: 'both', html: false }).find('[data-reply-look]').exists()).toBe(true);
  });

  it('says what the look is on the closed section: default, or each part that was chosen', () => {
    const plain = field({ target: 'overlay', html: false });

    expect(plain.find('[data-reply-look-trigger]').text()).toBe('LookDefault');

    const custom = field({
      target: 'overlay',
      html: false,
      look: {
        highlight: 'success',
        size: 'large',
        durationSeconds: 20,
      },
    });

    expect(custom.find('[data-reply-look-trigger]').text()).toBe('LookSuccess, Large, 20 s');
  });

  it('emits only the part that changed: a large size alone is a look with just the size', async () => {
    const wrapper = field({ target: 'overlay', html: false });

    await segment(wrapper, 'Large').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({
      target: 'overlay',
      html: false,
      look: { size: 'large' },
    });
  });

  it('emits the chosen highlight token', async () => {
    const wrapper = field({ target: 'both', html: true });

    await pickHighlight('Success');

    expect(lastUpdate(wrapper)).toEqual({
      target: 'both',
      html: true,
      look: { highlight: 'success' },
    });
  });

  it('emits the typed show time in seconds, and drops the whole look when it is set to 0:00 again', async () => {
    const wrapper = field({ target: 'overlay', html: false });

    await durationInput(wrapper).setValue('0:20');

    expect(lastUpdate(wrapper)).toEqual({
      target: 'overlay',
      html: false,
      look: { durationSeconds: 20 },
    });

    await wrapper.setProps({ modelValue: lastUpdate(wrapper) });
    await durationInput(wrapper).setValue('0:00');

    expect(lastUpdate(wrapper)).toEqual({ target: 'overlay', html: false });
  });

  it('stores nothing for text that is not a length', async () => {
    const wrapper = field({ target: 'overlay', html: false });

    await durationInput(wrapper).setValue('soon');

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });

  it('shows a saved show time as minutes and seconds', () => {
    const wrapper = field({
      target: 'overlay',
      html: false,
      look: { durationSeconds: 90 },
    });

    expect(durationInput(wrapper).element.value).toBe('1:30');
  });

  it('keeps the other parts of the look when one part changes', async () => {
    const wrapper = field({
      target: 'overlay',
      html: false,
      look: {
        highlight: 'warning',
        durationSeconds: 15,
      },
    });

    await segment(wrapper, 'Large').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({
      target: 'overlay',
      html: false,
      look: {
        highlight: 'warning',
        size: 'large',
        durationSeconds: 15,
      },
    });
  });

  it('drops the look on the way to a target that never reaches the overlay', async () => {
    const wrapper = field({
      target: 'both',
      html: true,
      look: {
        highlight: 'destructive',
        size: 'large',
      },
    });

    await segment(wrapper, 'Twitch chat').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({ target: 'chat', html: false });
  });

  it('keeps the look when the reply moves between the overlay targets', async () => {
    const wrapper = field({
      target: 'overlay',
      html: false,
      look: { size: 'large' },
    });

    await segment(wrapper, 'Both').trigger('click');

    expect(lastUpdate(wrapper)).toEqual({
      target: 'both',
      html: false,
      look: { size: 'large' },
    });
  });

  it('locks the look controls while the editor is locked', () => {
    const wrapper = field({ target: 'overlay', html: false }, true);

    expect(durationInput(wrapper).attributes('disabled')).toBeDefined();
    expect(segment(wrapper, 'Large').attributes('disabled')).toBeDefined();
    expect(document.body.querySelector('[data-reply-look-highlight]')?.hasAttribute('disabled')).toBe(true);
  });
});

describe('the reply look helpers', () => {
  it('reads a delivery without a look as the default look', () => {
    expect(lookOf({ target: 'overlay', html: false })).toEqual({
      highlight: null,
      size: 'normal',
      durationSeconds: null,
    });
  });

  it('reads the show time as a number, even when the API sends it as text', () => {
    expect(lookOf({
      target: 'overlay',
      html: false,
      look: { durationSeconds: '25' },
    }).durationSeconds).toBe(25);
  });

  it('leaves no look key for a default look or for a target without the overlay', () => {
    const defaults = lookOf({ html: false });

    expect(withLook({ target: 'overlay', html: false }, defaults)).toEqual({ target: 'overlay', html: false });

    expect(withLook({ target: 'chat', html: false }, { ...defaults, size: 'large' })).toEqual({
      target: 'chat',
      html: false,
    });
  });

  it('keeps a saved look in the form and sends only its set parts', () => {
    const saved: ReplyDelivery = {
      target: 'overlay',
      html: false,
      look: {
        highlight: 'primary',
        size: 'normal',
        durationSeconds: null,
      },
    };

    expect(deliveryOf(saved)).toEqual({
      target: 'overlay',
      html: false,
      look: { highlight: 'primary' },
    });

    expect(deliveryToSave(saved)).toEqual({
      target: 'overlay',
      html: false,
      look: { highlight: 'primary' },
    });
  });

  it('counts a changed look as a change', () => {
    const plain: ReplyDelivery = { target: 'overlay', html: false };
    const large: ReplyDelivery = {
      target: 'overlay',
      html: false,
      look: { size: 'large' },
    };

    expect(sameDelivery(plain, large)).toBe(false);

    expect(sameDelivery(plain, {
      target: 'overlay',
      html: false,
      look: { size: 'normal' },
    })).toBe(true);
  });
});

describe('the reply delivery helpers', () => {
  it('reads a command that never set a delivery as plain Twitch chat', () => {
    expect(deliveryOf(null)).toEqual({ target: 'chat', html: false });
    expect(deliveryOf(undefined)).toEqual({ target: 'chat', html: false });
  });

  it('keeps a saved delivery as it is and fills a missing target with chat', () => {
    expect(deliveryOf({ target: 'both', html: true })).toEqual({ target: 'both', html: true });
    expect(deliveryOf({ html: false })).toEqual({ target: 'chat', html: false });
  });

  it('saves plain Twitch chat as chat, because the bot reads null on an update as unchanged', () => {
    expect(deliveryToSave({ target: 'chat', html: false })).toEqual({ target: 'chat', html: false });
    expect(deliveryToSave({ target: 'overlay', html: true })).toEqual({ target: 'overlay', html: true });
  });

  it('compares two deliveries by what they send', () => {
    expect(sameDelivery({ target: 'chat', html: false }, { html: false })).toBe(true);
    expect(sameDelivery({ target: 'overlay', html: true }, { target: 'overlay', html: false })).toBe(false);
  });
});
