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

import { beforeEach, describe, expect, it, vi } from 'vitest';

import type * as HubModule from '../dashboardHub';
import { joinTwitchChat, sendTwitchChat } from '../dashboardHub';
import { useExternalChat } from '../useExternalChat';

vi.mock('../dashboardHub', () => ({
  startDashboardHub: vi.fn<typeof HubModule.startDashboardHub>(async () => undefined),
  joinTwitchChat: vi.fn<typeof HubModule.joinTwitchChat>(async () => ({
    success: true,
    error: null,
    errorCode: null,
    missingScope: null,
  })),
  leaveTwitchChat: vi.fn<typeof HubModule.leaveTwitchChat>(async () => undefined),
  sendTwitchChat: vi.fn<typeof HubModule.sendTwitchChat>(),
}));

describe('useExternalChat send', () => {
  beforeEach(() => {
    vi.mocked(sendTwitchChat).mockReset();
  });

  it('names the missing permission from the error code, whatever the words say', async () => {
    vi.mocked(sendTwitchChat).mockResolvedValue({
      success: false,
      error: 'Uw bericht kon niet worden verzonden.',
      errorCode: 'missing_scope',
      missingScope: 'user:write:chat',
    });

    const chat = useExternalChat('9001');

    const sent = await chat.send('hi');

    expect(sent).toBe(false);
    expect(chat.missingScope.value).toBe('user:write:chat');
    expect(chat.failure.value).toBe('Uw bericht kon niet worden verzonden.');
  });

  it('does not report a missing permission when the words mention the scope but the code differs', async () => {
    vi.mocked(sendTwitchChat).mockResolvedValue({
      success: false,
      error: 'Twitch refused user:write:chat for this channel.',
      errorCode: 'forbidden',
      missingScope: null,
    });

    const chat = useExternalChat('9001');

    await chat.send('hi');

    expect(chat.missingScope.value).toBeNull();
  });

  it('clears an earlier missing permission after a send that works', async () => {
    vi.mocked(sendTwitchChat).mockResolvedValueOnce({
      success: false,
      error: 'x',
      errorCode: 'missing_scope',
      missingScope: 'user:write:chat',
    });

    vi.mocked(sendTwitchChat).mockResolvedValueOnce({
      success: true,
      error: null,
      errorCode: null,
      missingScope: null,
    });

    const chat = useExternalChat('9001');

    await chat.send('hi');
    const sent = await chat.send('hi again');

    expect(sent).toBe(true);
    expect(chat.missingScope.value).toBeNull();
  });

  it('names the read permission when the join is refused for a missing scope', async () => {
    vi.mocked(joinTwitchChat).mockResolvedValueOnce({
      success: false,
      error: 'Missing required scope.',
      errorCode: 'missing_scope',
      missingScope: 'user:read:chat',
    });

    const chat = useExternalChat('9001');

    await vi.waitFor(() =>
      expect(chat.failure.value).toBe('Missing required scope.'));

    expect(chat.missingScope.value).toBe('user:read:chat');
  });
});
