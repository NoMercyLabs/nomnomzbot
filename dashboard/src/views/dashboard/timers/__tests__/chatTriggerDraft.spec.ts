// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import type { ChatTrigger } from '@/api/chatTriggers';

import { changeOf, draftOf } from '../components/chatTriggerDraft';

const saved = {
  id: 'a',
  pattern: 'words a',
  matchType: 'contains',
  caseSensitive: false,
  isEnabled: true,
  response: 'Hello there',
  pipelineId: null,
  cooldownSeconds: 30,
  minPermissionLevel: 'Everyone',
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
} as ChatTrigger;

describe('chat trigger reply mode', () => {
  it('opens a trigger without a pipeline in Simple mode', () => {
    expect(draftOf(saved).mode).toBe('simple');
  });

  it('sends only the picked pipeline when a simple trigger switches to Full pipeline', () => {
    const draft = {
      ...draftOf(saved),
      mode: 'full' as const,
      pipelineId: 'p1',
    };

    expect(changeOf(draft, saved)).toEqual({ pipelineId: 'p1' });
  });

  it('sends nothing for Full pipeline until a pipeline is picked', () => {
    const draft = {
      ...draftOf(saved),
      mode: 'full' as const,
      response: 'Edited text',
    };

    expect(changeOf(draft, saved)).toEqual({});
  });
});
