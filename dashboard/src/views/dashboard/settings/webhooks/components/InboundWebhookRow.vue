<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One inbound webhook: its name, the provider kind, the link the provider posts to (copy), how many calls arrived
// and when the last one did, and the on/off switch.
import Trash from '~icons/nnz/trash';

import type { InboundWebhook } from '@/api/webhooks';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useFormattedTime } from '@/composables/useTime';

const { can } = useCan();
const props = defineProps<{ endpoint: InboundWebhook; busy: boolean }>();

defineEmits<{
  toggle: [];
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
        <span :id="`inbound-${endpoint.id}`" class="text-sm font-medium wrap-break-word">{{ endpoint.name }}</span>
        <Badge variant="outline">{{ endpoint.adapter }}</Badge>
      </span>
      <span data-last-received class="block text-sm text-muted-foreground">
        {{
          lastReceived
            ? $t('dashboard.settings.webhooks.inbound.lastReceived', {
              count: Number(endpoint.receiveCount),
              when: lastReceived,
            })
            : $t('dashboard.settings.webhooks.inbound.neverReceived')
        }}
      </span>
    </div>
    <CopyLinkButton :url="endpoint.ingestUrl" :label="$t('dashboard.settings.webhooks.inbound.copyLink')" />
    <Button
      v-if="can('webhooks:inbound:write')"
      variant="ghost"
      size="icon"
      data-inbound-delete
      :aria-label="$t('dashboard.settings.webhooks.inbound.delete', { name: endpoint.name })"
      @click="$emit('remove')"
    >
      <Trash class="size-5" />
    </Button>
    <GatedSwitch
      action="webhooks:inbound:write"
      :model-value="endpoint.isEnabled"
      :disabled="busy"
      :aria-labelledby="`inbound-${endpoint.id}`"
      @update:model-value="$emit('toggle')"
    />
  </div>
</template>
