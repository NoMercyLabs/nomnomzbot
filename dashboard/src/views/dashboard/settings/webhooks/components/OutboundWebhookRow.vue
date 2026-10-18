<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One outbound webhook: its name, the address it posts to, how many events it follows, when it last delivered,
// and the on/off switch. An endpoint the bot switched off after repeated failures says why and offers to turn back on.
import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';
import { computed } from 'vue';

import type { OutboundWebhook } from '@/api/webhooks';
import EnabledState from '@/components/layout/EnabledState.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useTime } from '@/composables/useTime';

const { can } = useCan();
const props = defineProps<{ endpoint: OutboundWebhook; busy: boolean }>();

defineEmits<{
  toggle: [];
  reenable: [];
  edit: [];
  remove: [];
}>();

const time = useTime();
const address = computed(() => `${props.endpoint.fqdn}${props.endpoint.path ?? ''}`);

const switchedOff = computed(() => Boolean(props.endpoint.disabledAt));

const lastDelivery = computed(() =>
  props.endpoint.lastDeliveryAt
    ? time.dateTime(props.endpoint.lastDeliveryAt)
    : undefined);
</script>

<template>
  <div
    :data-outbound="endpoint.name"
    class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
  >
    <div class="min-w-0 flex-1">
      <span class="flex flex-wrap items-center gap-2">
        <span :id="`outbound-${endpoint.id}`" class="text-sm font-medium wrap-break-word">{{ endpoint.name }}</span>
        <Badge variant="outline">
          {{ $t('dashboard.settings.webhooks.outbound.events', { count: endpoint.subscribedEventTypes.length }) }}
        </Badge>
      </span>
      <span class="block font-mono text-sm text-muted-foreground wrap-break-word">{{ address }}</span>
      <span data-last-delivery class="block text-sm text-muted-foreground">
        {{
          lastDelivery
            ? $t('dashboard.settings.webhooks.outbound.lastDelivery', { when: lastDelivery })
            : $t('dashboard.settings.webhooks.outbound.neverDelivered')
        }}
      </span>
      <span v-if="switchedOff" data-switched-off class="block text-sm text-destructive">
        {{ $t('dashboard.settings.webhooks.outbound.switchedOff', { reason: endpoint.disabledReason ?? '' }) }}
      </span>
    </div>
    <Button v-if="switchedOff && can('webhooks:outbound:write')"
            variant="outline"
            data-outbound-reenable
            :disabled="busy"
            @click="$emit('reenable')"
    >
      {{ $t('dashboard.settings.webhooks.outbound.reenable') }}
    </Button>
    <div v-if="can('webhooks:outbound:write')" class="flex items-center gap-1">
      <Button
        variant="ghost"
        size="icon"
        data-outbound-edit
        :aria-label="$t('dashboard.settings.webhooks.outbound.edit', { name: endpoint.name })"
        @click="$emit('edit')"
      >
        <Edit class="size-5" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        data-outbound-delete
        :aria-label="$t('dashboard.settings.webhooks.outbound.delete', { name: endpoint.name })"
        @click="$emit('remove')"
      >
        <Trash class="size-5" />
      </Button>
    </div>
    <Switch
      v-if="can('webhooks:outbound:write')"
      :model-value="endpoint.isEnabled"
      :disabled="busy"
      :aria-labelledby="`outbound-${endpoint.id}`"
      @update:model-value="$emit('toggle')"
    />
    <EnabledState v-else :enabled="endpoint.isEnabled" />
  </div>
</template>
