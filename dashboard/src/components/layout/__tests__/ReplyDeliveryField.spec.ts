// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import ReplyDeliveryField from '@/components/layout/ReplyDeliveryField.vue';
import {
  deliveryOf,
  deliveryToSave,
  plainDelivery,
  sameDelivery,
  type ReplyDelivery,
} from '@/components/layout/replyMode';
import { i18n } from '@/i18n';

function field(modelValue: ReplyDelivery, disabled: boolean = false): VueWrapper {
  return mount(ReplyDeliveryField, {
    props: {
      modelValue,
      disabled,
    },
    global: { plugins: [i18n] },
  });
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

describe('the reply delivery helpers', () => {
  it('reads a command that never set a delivery as plain Twitch chat', () => {
    expect(deliveryOf(null)).toEqual({ target: 'chat', html: false });
    expect(deliveryOf(undefined)).toEqual({ target: 'chat', html: false });
  });

  it('keeps a saved delivery as it is and fills a missing target with chat', () => {
    expect(deliveryOf({ target: 'both', html: true })).toEqual({ target: 'both', html: true });
    expect(deliveryOf({ html: false })).toEqual({ target: 'chat', html: false });
  });

  it('saves plain Twitch chat as null, so the command keeps the behavior from before the setting', () => {
    expect(deliveryToSave({ target: 'chat', html: false })).toBeNull();
    expect(deliveryToSave({ target: 'overlay', html: true })).toEqual({ target: 'overlay', html: true });
  });

  it('compares two deliveries by what they send', () => {
    expect(sameDelivery({ target: 'chat', html: false }, { html: false })).toBe(true);
    expect(sameDelivery({ target: 'overlay', html: true }, { target: 'overlay', html: false })).toBe(false);
  });
});
