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

import ChatOverlay from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Bundles/chat_overlay/index.vue';
import ChatBox from '../../../server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/chat_box.vue';

// The bot's overlay line (messageType "overlay_line") is the reply of a command set to the chat overlay. Both
// chat widgets draw it under the bot's own name, with "requested by <viewer>" like the song request card, and
// apply the look the channel chose. The widgets are overlay SFCs served by the bot; this stub is the host SDK.
type Handler = (data: unknown) => void;

const handlers: Record<string, Handler> = {};

const sdk = {
  onSettings: () => sdk,
  on: (type: string, cb: Handler) => {
    handlers[type] = cb;

    return sdk;
  },
  off: (type: string) => {
    delete handlers[type];

    return sdk;
  },
};

interface OverlayLine {
  requestedBy: string | null;
  highlight: string | null;
  size: string;
  durationSeconds: number | null;
}

// The DashboardChatMessageDto the bot pushes for an overlay line (ChatOverlayLineSender).
type Line = Record<string, unknown>;

function botLine(overlayLine: OverlayLine | null, extra: Line = {}): Line {
  return {
    id: 'line-1',
    channelId: 'channel-1',
    userId: 'bot-1',
    displayName: 'NomNomzBot',
    username: 'NomNomzBot',
    message: 'CHOO CHOO',
    fragments: [
      {
        type: 'html',
        text: '<marquee>CHOO CHOO</marquee>',
      },
    ],
    userType: 'bot',
    isCommand: false,
    badges: [],
    bitsAmount: 0,
    messageType: 'overlay_line',
    timestamp: '2026-10-28T12:00:00.0000000Z',
    provider: 'twitch',
    ...extra,
    ...(overlayLine === null
      ? {}
      : { overlayLine }),
  };
}

const look = {
  requestedBy: 'Viewer One',
  highlight: 'success',
  size: 'large',
  durationSeconds: 20,
} as const satisfies OverlayLine;

const wrappers: VueWrapper[] = [];

function show(widget: typeof ChatBox | typeof ChatOverlay, line: Line): VueWrapper {
  const wrapper = mount(widget);

  wrappers.push(wrapper);
  handlers.ChatMessage!(line);

  return wrapper;
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('NomNomz', sdk);
});

afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount());
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('the chat box widget draws the bot overlay line', () => {
  it('shows the line under the bot name with "requested by" and the viewer name', async () => {
    const wrapper = show(ChatBox, botLine(look));

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.name').text()).toBe('NomNomzBot');
    expect(wrapper.find('.frag-html').html()).toContain('CHOO CHOO');
    expect(wrapper.find('.requested-by').text()).toBe('requested by Viewer One');
    expect(wrapper.find('.head').text()).not.toContain('Viewer One');
  });

  it('draws the requester as text, so a viewer name can never become markup', async () => {
    const wrapper = show(
      ChatBox,
      botLine({
        ...look,
        requestedBy: '<script>steal()</script>',
      }),
    );

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.requested-by').text()).toBe('requested by <script>steal()</script>');
    expect(wrapper.find('.requested-by script').exists()).toBe(false);
  });

  it('applies the chosen highlight token and size to the line', async () => {
    const wrapper = show(ChatBox, botLine(look));

    await vi.advanceTimersByTimeAsync(0);

    const line = wrapper.find('.line');

    expect(line.attributes('data-highlight')).toBe('success');
    expect(line.attributes('data-size')).toBe('large');
    expect(line.attributes('style')).toContain('oklch(0.795095 0.234563 145.534)');
  });

  it('fades the line after its own duration and then removes it, while an ordinary line stays', async () => {
    const wrapper = show(ChatBox, botLine(look));

    await vi.advanceTimersByTimeAsync(19_000);

    expect(wrapper.find('.line').classes()).not.toContain('faded');

    await vi.advanceTimersByTimeAsync(1_000);

    expect(wrapper.find('.line').classes()).toContain('faded');

    await vi.advanceTimersByTimeAsync(1_000);

    expect(wrapper.findAll('.line')).toHaveLength(0);
  });

  it('keeps the defaults when there is no look: no requester, normal size, no highlight, no fade', async () => {
    const wrapper = show(ChatBox, botLine(null));

    await vi.advanceTimersByTimeAsync(120_000);

    const line = wrapper.find('.line');

    expect(line.exists()).toBe(true);
    expect(line.attributes('data-size')).toBeUndefined();
    expect(line.attributes('data-highlight')).toBeUndefined();
    expect(line.classes()).not.toContain('faded');
    expect(wrapper.find('.requested-by').exists()).toBe(false);
  });

  it('ignores a highlight token it does not know and an empty requester', async () => {
    const wrapper = show(
      ChatBox,
      botLine({
        requestedBy: ' ',
        highlight: '#ff0000',
        size: 'normal',
        durationSeconds: null,
      }),
    );

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.line').attributes('data-highlight')).toBeUndefined();
    expect(wrapper.find('.requested-by').exists()).toBe(false);
  });
});

describe('the chat overlay bundle draws the bot overlay line', () => {
  it('shows the line under the bot name with "requested by" and the viewer name', async () => {
    const wrapper = show(ChatOverlay, botLine(look));

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.name').text()).toBe('NomNomzBot');
    expect(wrapper.find('.frag-html').html()).toContain('CHOO CHOO');
    expect(wrapper.find('.requested-by').text()).toBe('requested by Viewer One');
    expect(wrapper.find('.head').text()).not.toContain('Viewer One');
  });

  it('draws the requester as text, so a viewer name can never become markup', async () => {
    const wrapper = show(
      ChatOverlay,
      botLine({
        ...look,
        requestedBy: '<script>steal()</script>',
      }),
    );

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.requested-by').text()).toBe('requested by <script>steal()</script>');
    expect(wrapper.find('.requested-by script').exists()).toBe(false);
  });

  it('applies the chosen highlight token and size to the line', async () => {
    const wrapper = show(ChatOverlay, botLine(look));

    await vi.advanceTimersByTimeAsync(0);

    const line = wrapper.find('.message-container');

    expect(line.attributes('data-highlight')).toBe('success');
    expect(line.attributes('data-size')).toBe('large');
    expect(line.attributes('style')).toContain('oklch(0.795095 0.234563 145.534)');
  });

  it('leaves after its own duration instead of the widget show time', async () => {
    const wrapper = show(ChatOverlay, botLine(look));

    await vi.advanceTimersByTimeAsync(19_000);

    expect(wrapper.find('.message-container').classes()).not.toContain('leaving');

    await vi.advanceTimersByTimeAsync(1_000);

    expect(wrapper.find('.message-container').classes()).toContain('leaving');

    await vi.advanceTimersByTimeAsync(1_000);

    expect(wrapper.findAll('.message-container')).toHaveLength(0);
  });

  it('keeps the defaults when there is no look: widget show time, no requester, normal size', async () => {
    const wrapper = show(ChatOverlay, botLine(null));

    await vi.advanceTimersByTimeAsync(25_000);

    const line = wrapper.find('.message-container');

    expect(line.classes()).not.toContain('leaving');
    expect(line.attributes('data-size')).toBeUndefined();
    expect(line.attributes('data-highlight')).toBeUndefined();
    expect(wrapper.find('.requested-by').exists()).toBe(false);

    await vi.advanceTimersByTimeAsync(5_000);

    expect(wrapper.find('.message-container').classes()).toContain('leaving');
  });
});
