// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { AdminScheduledJob, AdminWebhookDelivery } from '@/api/adminActivity';
import { i18n } from '@/i18n';

import ProviderCredentialRow from '../components/ProviderCredentialRow.vue';
import ScheduledJobsTable from '../components/ScheduledJobsTable.vue';
import WebhookDeliveriesTable from '../components/WebhookDeliveriesTable.vue';

// The keys the signed-in admin holds; each case sets this before it mounts.
const held: { keys: string[] } = { keys: [] };

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({
    data: { value: { isAdmin: true, platformPermissions: held.keys } },
  }),
}));

vi.mock('@/api/adminProviders', () => ({
  useSaveProviderCredential: () => ({
    mutateAsync: vi.fn<() => void>(),
    isPending: { value: false },
  }),
  useClearProviderCredential: () => ({
    mutateAsync: vi.fn<() => void>(),
    isPending: { value: false },
  }),
}));

const global = { plugins: [i18n] };

const job = {
  id: 'j1',
  broadcasterId: 'b1',
  channelDisplayName: 'Stoney_Eagle',
  pipelineId: 'p1',
  pipelineName: 'Voice swap revert',
  pipelineExists: true,
  status: 'failed',
  displayState: 'failed',
  dueAt: '2026-10-01T10:00:00Z',
  createdAt: '2026-10-01T09:00:00Z',
  firedAt: null,
  triggeredByDisplayName: 'viewer_one',
  canRetry: true,
} as const satisfies AdminScheduledJob;

const delivery = {
  id: 7,
  broadcasterId: 'b1',
  endpointId: 'e1',
  endpointName: 'Discord hook',
  eventType: 'stream.online',
  status: 'failed',
  responseCode: 500,
  durationMs: 40,
  attempt: 1,
  createdAt: '2026-10-01T10:00:00Z',
  error: 'boom',
  endpointCanReplay: true,
} as const satisfies AdminWebhookDelivery;

const credential = {
  provider: 'twitch',
  supported: true,
  appDecisionRecorded: true,
  clientId: 'abc123',
  clientIdSource: 'database',
  secretSource: 'database',
  apiKeySource: null,
};

describe('admin action buttons need the matching platform key', () => {
  beforeEach(() => {
    held.keys = [];
  });

  it('shows the scheduled job retry button only with iam:manage', () => {
    const props = { rows: [job], retrying: false };

    const without = mount(ScheduledJobsTable, { props, global });

    expect(without.find('button').exists()).toBe(false);

    held.keys = ['iam:manage'];
    const withKey = mount(ScheduledJobsTable, { props, global });

    expect(withKey.findAll('button')).toHaveLength(1);
  });

  it('shows the webhook replay button only with iam:manage', async () => {
    const props = { rows: [delivery], replaying: false };

    const without = mount(WebhookDeliveriesTable, { props, global });

    expect(without.find('button').exists()).toBe(false);

    held.keys = ['iam:manage'];
    const withKey = mount(WebhookDeliveriesTable, { props, global });

    expect(withKey.findAll('button')).toHaveLength(1);

    await withKey.find('button').trigger('click');

    expect(withKey.emitted('replay')).toEqual([[7]]);
  });

  it('shows the provider save and clear buttons only with iam:manage', () => {
    const props = { credential };

    const without = mount(ProviderCredentialRow, { props, global });

    expect(without.find('[data-save]').exists()).toBe(false);
    expect(without.find('[data-clear]').exists()).toBe(false);

    held.keys = ['iam:manage'];
    const withKey = mount(ProviderCredentialRow, { props, global });

    expect(withKey.find('[data-save]').exists()).toBe(true);
    expect(withKey.find('[data-clear]').exists()).toBe(true);
  });
});
