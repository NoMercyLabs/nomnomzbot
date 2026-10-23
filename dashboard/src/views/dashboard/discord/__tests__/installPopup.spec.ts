// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { openInstallPopup, type InstallOutcome } from '../installPopup';

interface FakePopup {
  closed: boolean;
}

const url = 'https://bot.example.test/api/v1/channels/c1/integrations/discord/callback/start';

let popup: FakePopup;
let opened: ReturnType<typeof vi.spyOn>;

function relay(data: Record<string, string>, source: FakePopup = popup): void {
  window.dispatchEvent(
    new MessageEvent('message', {
      data: { type: 'oauth_relay', ...data },
      origin: 'https://bot.example.test',
      source: source as unknown as MessageEventSource,
    }),
  );
}

beforeEach(() => {
  vi.useFakeTimers();
  popup = { closed: false };
  opened = vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
});

afterEach(() => {
  vi.useRealTimers();
  opened.mockRestore();
});

describe('the Discord install popup', () => {
  it('opens the install address in its own small window', () => {
    openInstallPopup(url, vi.fn<(outcome: InstallOutcome) => void>());

    expect(opened).toHaveBeenCalledWith(url, 'nnz-discord-install', expect.stringContaining('width='));
  });

  it('reports a blocked popup so the caller can navigate instead', () => {
    opened.mockReturnValue(null);

    expect(openInstallPopup(url, vi.fn<(outcome: InstallOutcome) => void>())).toBe(false);
  });

  it('settles with no error when the relay page says the bot was added', () => {
    const settled = vi.fn<(outcome: InstallOutcome) => void>();

    openInstallPopup(url, settled);

    relay({ discord_connected: 'true' });

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: null });
  });

  it('settles with the reason when the relay page reports a failure', () => {
    const settled = vi.fn<(outcome: InstallOutcome) => void>();

    openInstallPopup(url, settled);

    relay({ discord_error: 'token_exchange_failed' });

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: 'token_exchange_failed' });
  });

  it('ignores a message from any other window', () => {
    const settled = vi.fn<(outcome: InstallOutcome) => void>();

    openInstallPopup(url, settled);

    relay({ discord_error: 'invalid_state' }, { closed: false });

    expect(settled).not.toHaveBeenCalled();
  });

  it('settles with no error, once, when the person closes the popup themselves', () => {
    const settled = vi.fn<(outcome: InstallOutcome) => void>();

    openInstallPopup(url, settled);

    popup.closed = true;
    vi.advanceTimersByTime(1500);

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: null });
  });
});
