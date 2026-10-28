<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one place for the overlays the bot cannot work without (alerts, audio, TTS caption, raid recap).
// Each is one browser source in OBS. The first one not loaded yet gets the one primary button; the rest are outline.
// The address holds a secret token: it lives in the setup dialog's Copy button and the page never prints it.
// Each row says Connect until the overlay is loaded in OBS, then Configure; both open the setup dialog.
// The id is the anchor a setup guide links to: /app/<channel>/obs#required-overlays.
import { computed } from 'vue';

import { readObsBridgeAddress, useObsBridge } from '@/api/obs';
import type { ObsBrowserSource, RequiredOverlay } from '@/api/widgets';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import ObsSetupAction from '@/views/dashboard/overlays/components/ObsSetupAction.vue';
import { overlayHealth } from '@/views/dashboard/overlays/components/overlayHealth';

import { sourceReasonKey } from '../obsHealth';

const props = defineProps<{
  overlays: readonly RequiredOverlay[] | undefined;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

// The first surface that is switched on but not open in OBS: the one thing to do next.
const nextKey = computed<string | null>(
  () =>
    props.overlays?.find(
      (overlay: RequiredOverlay) =>
        overlay.widget.isEnabled && !overlay.widget.isAttached && !!overlay.widget.overlayUrl,
    )?.key ?? null,
);

// The OBS control bridge is a browser source too. Its address is read when Copy is pressed, never kept on the page.
// The bridge page is fixed, not a catalog widget: a 1 x 1 invisible page that must stay open to keep the
// control link alive (ObsBridgeHostController.cs), so audio and shutdown are both off.
const BRIDGE_SETUP = {
  width: 1,
  height: 1,
  controlAudioViaObs: false,
  shutdownWhenHidden: false,
} as const satisfies ObsBrowserSource;
const { channelId } = useCurrentChannel();
const { can } = useCan();
const canCopyBridge = computed<boolean>(() => can('obs:config:write'));
const bridgeChannelId = computed<string | null>(() =>
  can('obs:config:read')
    ? channelId.value
    : null,
);
const bridge = useObsBridge(bridgeChannelId);
const bridgeOpen = computed<boolean>(() => Number(bridge.data.value?.instanceCount ?? 0) > 0);
const bridgeDot = computed<string>(() =>
  bridgeOpen.value
    ? 'ok'
    : 'warn',
);
const bridgeMeta = computed<string>(() => {
  if (!canCopyBridge.value)
    return 'dashboard.obs.required.bridge.locked';

  return bridgeOpen.value
    ? 'dashboard.obs.sources.loaded'
    : 'dashboard.obs.sources.notLoaded';
});
const bridgeIsNext = computed<boolean>(
  () => nextKey.value === null && !bridgeOpen.value && canCopyBridge.value,
);

async function bridgeAddress(): Promise<string | null | undefined> {
  if (!channelId.value)
    return null;

  return readObsBridgeAddress(channelId.value);
}
</script>

<template>
  <PanelCard
    id="required-overlays"
    data-required-overlays
    :title="$t('dashboard.obs.required.title')"
    :description="$t('dashboard.obs.required.description')"
    :state="panelState"
    :empty-text="$t('dashboard.obs.required.empty')"
    :error-text="$t('dashboard.obs.required.loadError')"
    @retry="$emit('retry')"
  >
    <div class="px-1 py-0.5">
      <SummaryRow
        v-for="overlay in overlays"
        :key="overlay.key"
        :data-required-overlay="overlay.key"
        :title="$t(`dashboard.obs.required.surfaces.${overlay.key}.name`)"
        :detail="$t(`dashboard.obs.required.surfaces.${overlay.key}.detail`)"
        :meta="$t(sourceReasonKey(overlay.widget))"
      >
        <template #leading>
          <span data-obs-dot="required" class="dot" :class="overlayHealth(overlay.widget)" aria-hidden="true" />
        </template>
        <template #trailing>
          <ObsSetupAction
            :title="$t(`dashboard.obs.required.surfaces.${overlay.key}.name`)"
            :setup="overlay.widget.obsSetup"
            :connected="overlay.widget.isAttached"
            :expected="overlay.widget.isEnabled"
            :primary="overlay.key === nextKey"
            :resolve-address="async () => overlay.widget.overlayUrl"
          />
        </template>
      </SummaryRow>
      <SummaryRow
        data-required-bridge
        :title="$t('dashboard.obs.required.bridge.name')"
        :detail="$t('dashboard.obs.required.bridge.detail')"
        :meta="$t(bridgeMeta)"
      >
        <template #leading>
          <span data-obs-dot="required" class="dot" :class="bridgeDot" aria-hidden="true" />
        </template>
        <template #trailing>
          <ObsSetupAction
            :title="$t('dashboard.obs.required.bridge.name')"
            :setup="BRIDGE_SETUP"
            :connected="bridgeOpen"
            :primary="bridgeIsNext"
            :disabled="!canCopyBridge"
            :resolve-address="bridgeAddress"
          />
        </template>
      </SummaryRow>
    </div>
  </PanelCard>
</template>
