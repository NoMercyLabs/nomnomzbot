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

const settingsListeners: ((settings: Record<string, unknown>) => void)[] = [];

const sdk = {
  onSettings: (cb: (settings: Record<string, unknown>) => void) => {
    settingsListeners.push(cb);

    return sdk;
  },
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

function sendSettings(settings: Record<string, unknown>): void {
  settingsListeners.forEach((cb) => cb(settings));
}

beforeEach(() => {
  settingsListeners.splice(0);
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

describe('the chat box widget role badge option', () => {
  const moderatorLine = botLine(null, {
    userType: 'viewer',
    displayName: 'Mod One',
    isModerator: true,
  });

  const plainLine = botLine(null, {
    userType: 'viewer',
    displayName: 'Viewer Two',
  });

  it('draws no role badge by default', async () => {
    const wrapper = show(ChatBox, moderatorLine);

    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.role-badge').exists()).toBe(false);
  });

  it('draws one badge with the role in front of the name when the option is on', async () => {
    const wrapper = mount(ChatBox);

    wrappers.push(wrapper);
    sendSettings({ showRoleBadge: true });
    handlers.ChatMessage!(moderatorLine);
    await vi.advanceTimersByTimeAsync(0);

    const badges = wrapper.findAll('.line .role-badge');

    expect(badges).toHaveLength(1);
    expect(badges[0]!.text()).toBe('moderator');
    expect(badges[0]!.classes()).toContain('role-badge-moderator');
    expect(wrapper.find('.head').text()).toContain('moderatorMod One');
  });

  it('draws no badge on a line without a role, even when the option is on', async () => {
    const wrapper = mount(ChatBox);

    wrappers.push(wrapper);
    sendSettings({ showRoleBadge: true });
    handlers.ChatMessage!(plainLine);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.line').exists()).toBe(true);
    expect(wrapper.find('.role-badge').exists()).toBe(false);
  });

  it('removes the badge again when the option goes back to off', async () => {
    const wrapper = mount(ChatBox);

    wrappers.push(wrapper);
    sendSettings({ showRoleBadge: true });
    handlers.ChatMessage!(moderatorLine);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.findAll('.role-badge')).toHaveLength(1);

    sendSettings({ showRoleBadge: false });
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.role-badge').exists()).toBe(false);
  });
});

// A chat line appears together with its emote and badge images: it is held until every image is loaded and
// decoded, or until the hold time (400 ms) passes. The browser Image is the outside world, so a stub stands in
// for it and the test settles each decode by hand.
describe('the chat box widget holds a line until its images are decoded', () => {
  interface PendingImage {
    src: string;
    settle: (failed: boolean) => void;
  }

  const images: PendingImage[] = [];

  class StubImage {
    src = '';

    decode(): Promise<void> {
      return new Promise<void>((resolve, reject) => {
        images.push({
          src: this.src,
          settle: (failed: boolean) =>
            failed
              ? reject(new Error('decode failed'))
              : resolve(),
        });
      });
    }
  }

  function emoteFragment(name: string): Line {
    return {
      type: 'emote',
      text: name,
      emote: { urls: { 2: `https://cdn.test/${name}.png` } },
    };
  }

  function chatter(id: string, extra: Line = {}): Line {
    return botLine(null, {
      id,
      userId: `user-${id}`,
      userType: 'viewer',
      displayName: `Chatter ${id}`,
      message: `message ${id}`,
      fragments: [{ type: 'text', text: `message ${id}` }],
      ...extra,
    });
  }

  function emoteLine(id: string, extra: Line = {}): Line {
    return chatter(id, {
      fragments: [emoteFragment('Kappa'), emoteFragment('PogChamp')],
      ...extra,
    });
  }

  function mountBox(): VueWrapper {
    const wrapper = mount(ChatBox);

    wrappers.push(wrapper);

    return wrapper;
  }

  function lineIds(wrapper: VueWrapper): string[] {
    return wrapper.findAll('.line').map((l) => l.find('.name').text());
  }

  beforeEach(() => {
    images.splice(0);
    vi.stubGlobal('Image', StubImage);
  });

  it('keeps a line with two emotes out of the page until both images are decoded', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    await vi.advanceTimersByTimeAsync(100);

    expect(images.map((i) => i.src)).toEqual(['https://cdn.test/Kappa.png', 'https://cdn.test/PogChamp.png']);
    expect(wrapper.find('.line').exists()).toBe(false);

    images[0]!.settle(false);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.line').exists()).toBe(false);

    images[1]!.settle(false);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.findAll('.line')).toHaveLength(1);
    expect(wrapper.findAll('.line img.emote')).toHaveLength(2);
    expect(wrapper.find('.line img.emote').attributes('decoding')).toBe('sync');
  });

  it('draws the line after the hold time when the images never settle', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    await vi.advanceTimersByTimeAsync(399);

    expect(wrapper.find('.line').exists()).toBe(false);

    await vi.advanceTimersByTimeAsync(2);

    expect(wrapper.findAll('.line')).toHaveLength(1);
  });

  it('starts the hold when a line arrives, so a burst of slow lines waits one hold time and not one each', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    handlers.ChatMessage!(emoteLine('b', { fragments: [emoteFragment('LUL')] }));
    handlers.ChatMessage!(emoteLine('c', { fragments: [emoteFragment('HeyGuys')] }));
    await vi.advanceTimersByTimeAsync(0);

    expect(images).toHaveLength(4);

    await vi.advanceTimersByTimeAsync(401);

    expect(lineIds(wrapper)).toEqual(['Chatter a', 'Chatter b', 'Chatter c']);
  });

  it('holds a line for its cheermote image', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(
      chatter('a', {
        fragments: [
          {
            type: 'cheermote',
            text: 'Cheer100',
            cheermote: { urls: { 2: 'https://cdn.test/cheer100.gif' } },
          },
        ],
      }),
    );

    await vi.advanceTimersByTimeAsync(0);

    expect(images.map((i) => i.src)).toEqual(['https://cdn.test/cheer100.gif']);
    expect(wrapper.find('.line').exists()).toBe(false);

    images[0]!.settle(false);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.findAll('.line img.cheermote')).toHaveLength(1);
    expect(wrapper.find('.line img.cheermote').attributes('decoding')).toBe('sync');
  });

  it('counts an image that fails to decode as ready', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    await vi.advanceTimersByTimeAsync(0);
    images.forEach((i) => i.settle(true));
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.findAll('.line')).toHaveLength(1);
  });

  it('keeps chat order: a line without images waits behind an earlier held line', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    handlers.ChatMessage!(chatter('b'));
    await vi.advanceTimersByTimeAsync(100);

    expect(wrapper.find('.line').exists()).toBe(false);

    images.forEach((i) => i.settle(false));
    await vi.advanceTimersByTimeAsync(0);

    expect(lineIds(wrapper)).toEqual(['Chatter a', 'Chatter b']);
  });

  it('never draws a held line whose message was deleted before its images were ready', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    handlers.ChatMessage!(chatter('b'));
    await vi.advanceTimersByTimeAsync(0);
    handlers.MessageDeleted!({ messageId: 'a' });
    images.forEach((i) => i.settle(false));
    await vi.advanceTimersByTimeAsync(0);

    expect(lineIds(wrapper)).toEqual(['Chatter b']);
  });

  it('never draws a held line whose chatter was cleared before its images were ready', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(emoteLine('a'));
    await vi.advanceTimersByTimeAsync(0);
    handlers.UserMessagesCleared!({ targetUserId: 'user-a' });
    images.forEach((i) => i.settle(false));
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.find('.line').exists()).toBe(false);
  });

  it('draws a line without images at once, with no wait', async () => {
    const wrapper = mountBox();

    handlers.ChatMessage!(chatter('a'));
    await vi.advanceTimersByTimeAsync(0);

    expect(images).toHaveLength(0);
    expect(lineIds(wrapper)).toEqual(['Chatter a']);
  });

  it('does not wait for emote images when emotes are off', async () => {
    const wrapper = mountBox();

    sendSettings({ showEmotes: false });
    handlers.ChatMessage!(emoteLine('a'));
    await vi.advanceTimersByTimeAsync(0);

    expect(images).toHaveLength(0);
    expect(wrapper.findAll('.line')).toHaveLength(1);
    expect(wrapper.find('img.emote').exists()).toBe(false);
  });

  it('holds a line for its badge images when badges are on, and not when they are off', async () => {
    const badgeLine = chatter('a', { badges: [{ urls: { 2: 'https://cdn.test/mod.png' } }] });
    const wrapper = mountBox();

    handlers.ChatMessage!(badgeLine);
    await vi.advanceTimersByTimeAsync(0);

    expect(images.map((i) => i.src)).toEqual(['https://cdn.test/mod.png']);
    expect(wrapper.find('.line').exists()).toBe(false);

    images[0]!.settle(false);
    await vi.advanceTimersByTimeAsync(0);

    expect(wrapper.findAll('.line img.badge')).toHaveLength(1);
    expect(wrapper.find('.line img.badge').attributes('decoding')).toBe('sync');

    images.splice(0);
    sendSettings({ showBadges: false });
    handlers.ChatMessage!(chatter('b', { badges: [{ urls: { 2: 'https://cdn.test/mod.png' } }] }));
    await vi.advanceTimersByTimeAsync(0);

    expect(images).toHaveLength(0);
    expect(wrapper.findAll('.line')).toHaveLength(2);
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
