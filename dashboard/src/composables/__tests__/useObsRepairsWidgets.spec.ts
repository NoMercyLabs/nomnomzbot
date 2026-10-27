// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { useObsRepairsWidgets } from '../useObsRepairsWidgets';

const status = ref<{ isEnabled: boolean; mode: string } | null>(null);
const bridge = ref<{ instanceCount: number } | null>(null);
const canReadConfig = ref<boolean>(true);

vi.mock('@/api/obs', () => ({
  useObsConnectionStatus: () => ({ data: status }),
  useObsBridge: () => ({ data: bridge }),
}));

vi.mock('../useCan', () => ({
  useCan: () => ({ can: () => canReadConfig.value }),
}));

vi.mock('../useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref<string | null>('channel-1') }),
}));

beforeEach(() => {
  status.value = null;
  bridge.value = null;
  canReadConfig.value = true;
});

describe('useObsRepairsWidgets', () => {
  it('is false while OBS is switched off or not yet read', () => {
    const repairs = useObsRepairsWidgets();

    expect(repairs.value).toBe(false);

    status.value = { isEnabled: false, mode: 'direct' };
    expect(repairs.value).toBe(false);
  });

  it('is true when the direct OBS connection is switched on', () => {
    status.value = { isEnabled: true, mode: 'direct' };

    expect(useObsRepairsWidgets().value).toBe(true);
  });

  it('needs an open bridge page in bridge mode', () => {
    status.value = { isEnabled: true, mode: 'bridge' };
    const repairs = useObsRepairsWidgets();

    expect(repairs.value).toBe(false);

    bridge.value = { instanceCount: 1 };
    expect(repairs.value).toBe(true);
  });

  it('gives the benefit of the doubt to someone who cannot read the bridge', () => {
    canReadConfig.value = false;
    status.value = { isEnabled: true, mode: 'bridge' };

    expect(useObsRepairsWidgets().value).toBe(true);
  });
});
