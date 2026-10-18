<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The VTube Studio link: where the bot dials, whether the one-time approval is done, and the bridge credential.
// Save is the one primary action; Authorize and Rotate are outline, and rotating asks first.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useVtsAuthorize,
  useVtsRotateBridgeToken,
  useVtsSaveConnection,
  type VtsConnection,
} from '@/api/vts';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastVtsError } from '../vtsToast';
import { vtsHealth, type VtsHealth } from '../vtsHealth';

const props = defineProps<{
  connection: VtsConnection | null | undefined;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useVtsSaveConnection(channelId);
const authorize = useVtsAuthorize(channelId);
const rotate = useVtsRotateBridgeToken(channelId);

const mode = ref<string>('direct');
const endpoint = ref<string>('');
const mask = ref<string>('');
const enabled = ref<boolean>(false);
const confirming = ref<boolean>(false);

watch(
  () => props.connection,
  (connection) => {
    mode.value = connection?.mode ?? 'direct';
    endpoint.value = connection?.endpoint ?? '';
    mask.value = String(connection?.eventSubscriptionsMask ?? '');
    enabled.value = connection?.isEnabled === true;
  },
  { immediate: true },
);

const health = computed<VtsHealth>(() => vtsHealth(props.connection));
const modes = computed(() => [
  { value: 'direct', label: t('dashboard.vts.connection.modes.direct') },
  { value: 'bridge', label: t('dashboard.vts.connection.modes.bridge') },
]);
const tokenText = computed<string>(() =>
  t(props.connection?.hasPluginToken
    ? 'dashboard.vts.connection.tokenStored'
    : 'dashboard.vts.connection.tokenMissing'),
);

function submit(): void {
  save.mutate({
    mode: mode.value,
    endpoint: endpoint.value,
    eventSubscriptionsMask: Number.parseInt(mask.value, 10),
    isEnabled: enabled.value,
  }, { onError: toastVtsError });
}

function rotateNow(): void {
  rotate.mutate(undefined, {
    onSuccess: () => {
      confirming.value = false;
    },
    onError: toastVtsError,
  });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.vts.connection.title')"
    :state="panelState"
    :error-text="$t('dashboard.vts.connection.loadError')"
    @retry="$emit('retry')"
  >
    <div data-vts-connection class="px-1 py-0.5">
      <SummaryRow
        :title="$t('dashboard.vts.connection.link')"
        :detail="$t(`dashboard.vts.status.${health.reason}`)"
      >
        <template #detail-lead>
          <span data-vts-dot class="dot" :class="health.dot" aria-hidden="true" />
        </template>
      </SummaryRow>
      <SummaryRow :title="$t('dashboard.vts.connection.token')" :detail="tokenText">
        <template #detail-lead>
          <span
            data-vts-token-dot
            class="dot"
            :class="connection?.hasPluginToken ? 'ok' : 'warn'"
            aria-hidden="true"
          />
        </template>
      </SummaryRow>
    </div>
    <form class="grid gap-3 p-3" @submit.prevent="submit">
      <label class="flex items-center justify-between gap-3 text-sm font-medium">
        {{ $t('dashboard.vts.connection.enabled') }}
        <Switch
          v-model="enabled"
          data-vts-enabled
          :aria-label="$t('dashboard.vts.connection.enabled')"
        />
      </label>
      <SegmentedControl
        v-model="mode"
        data-vts-mode
        :label="$t('dashboard.vts.connection.mode')"
        :options="modes"
      />
      <label class="grid gap-1 text-sm text-muted-foreground">
        {{ $t('dashboard.vts.connection.endpoint') }}
        <Input v-model="endpoint" data-vts-endpoint />
      </label>
      <label class="grid gap-1 text-sm text-muted-foreground">
        {{ $t('dashboard.vts.connection.mask') }}
        <Input v-model="mask" data-vts-mask type="number" min="0" />
        <span>{{ $t('dashboard.vts.connection.maskHelp') }}</span>
      </label>
      <div class="flex flex-wrap justify-end gap-2">
        <Button
          v-if="mode === 'bridge'"
          data-vts-rotate
          type="button"
          variant="outline"
          @click="confirming = true"
        >
          {{ $t('dashboard.vts.connection.rotate') }}
        </Button>
        <Button
          data-vts-authorize
          type="button"
          variant="outline"
          :disabled="authorize.isPending.value"
          @click="authorize.mutate(undefined, { onError: toastVtsError })"
        >
          <Spinner v-if="authorize.isPending.value" />
          {{ authorize.isPending.value ? $t('dashboard.vts.connection.waiting') : $t('dashboard.vts.connection.authorize') }}
        </Button>
        <Button data-vts-save type="submit" :disabled="save.isPending.value">
          {{ $t('dashboard.vts.connection.save') }}
        </Button>
      </div>
      <p v-if="authorize.isPending.value" class="text-sm text-muted-foreground">
        {{ $t('dashboard.vts.connection.waitingHelp') }}
      </p>
    </form>
    <ConfirmDialog
      v-model:open="confirming"
      :title="$t('dashboard.vts.connection.rotateConfirm.title')"
      :description="$t('dashboard.vts.connection.rotateConfirm.description')"
      :consequences="[$t('dashboard.vts.connection.rotateConfirm.old')]"
      :action="$t('dashboard.vts.connection.rotateConfirm.action')"
      :pending="rotate.isPending.value"
      @confirm="rotateNow"
    />
  </PanelCard>
</template>
