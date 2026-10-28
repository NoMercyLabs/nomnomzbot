<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One inbound webhook: its name, the provider kind, the link the provider posts to (copy), how many calls arrived
// and when the last one did, and the on/off switch.
import Edit from '~icons/nnz/edit';
import Rotate from '~icons/nnz/rotate';
import Trash from '~icons/nnz/trash';

import type { InboundWebhook } from '@/api/webhooks';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import IconAction from '@/components/layout/IconAction.vue';
import { Badge } from '@/components/ui/badge';
import { useCan } from '@/composables/useCan';
import { useFormattedTime } from '@/composables/useTime';

const { can } = useCan();
const props = defineProps<{ endpoint: InboundWebhook; busy: boolean }>();

defineEmits<{
  toggle: [];
  edit: [];
  rotate: [];
  remove: [];
}>();

const lastReceived = useFormattedTime(() => props.endpoint.lastReceivedAt, 'dateTime');
</script>

<template>
  <div
    :data-inbound="endpoint.name"
    class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
  >
    <div class="min-w-0 flex-1">
      <span class="flex flex-wrap items-center gap-2">
        <span :id="`inbound-${endpoint.id}`" class="text-sm font-medium wrap-break-word">{{
          endpoint.name
        }}</span>
        <Badge variant="outline">{{ endpoint.adapter }}</Badge>
      </span>
      <span data-last-received class="block text-sm text-muted-foreground">
        {{
          lastReceived
            ? $t("dashboard.settings.webhooks.inbound.lastReceived", {
              count: Number(endpoint.receiveCount),
              when: lastReceived,
            })
            : $t("dashboard.settings.webhooks.inbound.neverReceived")
        }}
      </span>
    </div>
    <!-- Narrow screens give the actions their own line under the text, so the name never squeezes. -->
    <div class="flex w-full flex-wrap items-center justify-end gap-2 sm:w-auto sm:flex-nowrap">
      <div data-inbound-actions class="flex items-center gap-1">
        <CopyLinkButton
          class="border-border"
          :url="endpoint.ingestUrl"
          :label="$t('dashboard.settings.webhooks.inbound.copyLink')"
        />
        <IconAction
          v-if="can('webhooks:inbound:write')"
          data-inbound-edit
          :label="$t('dashboard.settings.webhooks.inbound.edit', { name: endpoint.name })"
          @click="$emit('edit')"
        >
          <Edit class="size-5" />
        </IconAction>
        <IconAction
          v-if="can('webhooks:inbound:write')"
          data-inbound-rotate
          :label="$t('dashboard.settings.webhooks.inbound.rotate', { name: endpoint.name })"
          @click="$emit('rotate')"
        >
          <Rotate class="size-5" />
        </IconAction>
        <IconAction
          v-if="can('webhooks:inbound:write')"
          data-inbound-delete
          :label="$t('dashboard.settings.webhooks.inbound.delete', { name: endpoint.name })"
          @click="$emit('remove')"
        >
          <Trash class="size-5" />
        </IconAction>
        <GatedSwitch
          class="ml-2"
          action="webhooks:inbound:write"
          :model-value="endpoint.isEnabled"
          :disabled="busy"
          :aria-label="$t('dashboard.settings.webhooks.inbound.toggle', { name: endpoint.name })"
          @update:model-value="$emit('toggle')"
        />
      </div>
    </div>
  </div>
</template>
