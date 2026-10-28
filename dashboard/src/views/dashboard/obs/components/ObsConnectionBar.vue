<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The OBS link on one line at the top of the page: the saved connection and the browser-source bridge,
// each a dot (green works, orange needs a look, red failed, gray off) with the reason, and Edit connection.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ObsBridgeStatus, ObsConnection } from '@/api/obs';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCan } from '@/composables/useCan';
import type { PanelState } from '@/composables/usePanelState';
import Settings from '~icons/nnz/settings';

import ObsConnectionForm from './ObsConnectionForm.vue';
import { bridgeHealth, connectionHealth, type Health } from '../obsHealth';

const props = defineProps<{
  connection: ObsConnection | null | undefined;
  bridge: ObsBridgeStatus | null | undefined;
  unreachable: boolean;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

const { t } = useI18n();
const { can } = useCan();

const editing = ref<boolean>(false);

const link = computed<Health>(() => connectionHealth(props.connection, props.unreachable));
const bridgeRow = computed<Health>(() => bridgeHealth(props.connection, props.bridge));

// While OBS is not linked, linking it is the one thing to do on the page, so the button leads.
const linked = computed<boolean>(() => props.connection?.isEnabled === true);

// The reason a connection failed, or its address, when the bot has them.
const linkMeta = computed<string | undefined>(() => {
  if (link.value.dot === 'bad')
    return props.connection?.lastError ?? undefined;

  const host = props.connection?.host;
  const port = props.connection?.port;

  if (!host)
    return undefined;

  return port
    ? `${host}:${port}`
    : host;
});

const bridgeDetail = computed<string>(() =>
  t(`dashboard.obs.${bridgeRow.value.reason}`, { count: Number(props.bridge?.instanceCount ?? 0) }),
);
</script>

<template>
  <section
    data-obs-connection
    :aria-label="$t('dashboard.obs.connection.title')"
    class="flex min-h-12 flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border border-border bg-card px-3 py-2"
  >
    <template v-if="panelState === 'error'">
      <p class="text-sm text-muted-foreground">{{ $t('dashboard.obs.connection.loadError') }}</p>
      <Button
        class="ms-auto"
        variant="outline"
        size="sm"
        @click="$emit('retry')"
      >
        {{ $t('common.panel.retry') }}
      </Button>
    </template>
    <Skeleton v-else-if="panelState === 'loading'" class="h-5 w-72" />
    <template v-else>
      <p class="flex min-w-0 items-center gap-2 text-sm">
        <span data-obs-dot="connection" class="dot" :class="link.dot" aria-hidden="true" />
        <span class="font-medium">{{ $t('dashboard.obs.connection.obs') }}</span>
        <span class="text-muted-foreground">{{ $t(`dashboard.obs.${link.reason}`) }}</span>
        <span v-if="linkMeta" data-obs-link-meta class="truncate text-muted-foreground tabular-nums">{{ linkMeta }}</span>
      </p>
      <p class="flex min-w-0 items-center gap-2 text-sm">
        <span data-obs-dot="bridge" class="dot" :class="bridgeRow.dot" aria-hidden="true" />
        <span class="font-medium">{{ $t('dashboard.obs.bridge.title') }}</span>
        <span class="text-muted-foreground">{{ bridgeDetail }}</span>
      </p>
      <Button
        v-if="can('obs:config:write')"
        data-obs-edit-connection
        class="ms-auto"
        size="sm"
        :variant="linked
          ? 'outline'
          : 'default'"
        @click="editing = true"
      >
        <Settings aria-hidden="true" />
        {{ $t(linked
          ? 'dashboard.obs.connection.edit'
          : 'dashboard.obs.connection.link') }}
      </Button>
    </template>
  </section>
  <ObsConnectionForm v-model:open="editing" :connection="connection" />
</template>
