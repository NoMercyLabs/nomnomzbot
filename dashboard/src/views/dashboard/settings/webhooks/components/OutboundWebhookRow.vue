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
import History from '~icons/nnz/clock-backward';
import Edit from '~icons/nnz/edit';
import Key from '~icons/nnz/key-modern';
import Send from '~icons/nnz/paper-plane-send';
import Trash from '~icons/nnz/trash';
import { computed } from 'vue';

import type { OutboundWebhook } from '@/api/webhooks';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import IconAction from '@/components/layout/IconAction.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useTime } from '@/composables/useTime';

const { can } = useCan();
const props = defineProps<{ endpoint: OutboundWebhook; busy: boolean }>();

defineEmits<{
  toggle: [];
  reenable: [];
  edit: [];
  test: [];
  rotate: [];
  deliveries: [];
  remove: [];
}>();

const time = useTime();
const address = computed(() => `${props.endpoint.fqdn}${props.endpoint.path ?? ''}`);

const switchedOff = computed(() => Boolean(props.endpoint.disabledAt));

const lastDelivery = computed(() =>
  props.endpoint.lastDeliveryAt
    ? time.dateTime(props.endpoint.lastDeliveryAt)
    : undefined,
);
</script>

<template>
  <div
    :data-outbound="endpoint.name"
    class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
  >
    <div class="min-w-0 flex-1">
      <span class="flex flex-wrap items-center gap-2">
        <span :id="`outbound-${endpoint.id}`" class="text-sm font-medium wrap-break-word">{{
          endpoint.name
        }}</span>
        <Badge variant="outline">
          {{
            $t("dashboard.settings.webhooks.outbound.events", {
              count: endpoint.subscribedEventTypes.length,
            })
          }}
        </Badge>
        <Badge
          v-if="switchedOff"
          data-switched-off-badge
          variant="outline"
          class="border-destructive/50 text-destructive"
        >
          {{ $t("dashboard.settings.webhooks.outbound.switchedOff") }}
        </Badge>
      </span>
      <span class="block font-mono text-sm text-muted-foreground wrap-break-word">{{
        address
      }}</span>
      <span
        v-if="lastDelivery || !switchedOff"
        data-last-delivery
        class="block text-sm text-muted-foreground"
      >
        {{
          lastDelivery
            ? $t("dashboard.settings.webhooks.outbound.lastDelivery", { when: lastDelivery })
            : $t("dashboard.settings.webhooks.outbound.neverDelivered")
        }}
      </span>
      <span
        v-if="switchedOff && endpoint.disabledReason"
        data-switched-off
        class="block text-sm text-muted-foreground"
      >
        {{ endpoint.disabledReason }}
      </span>
    </div>
    <!-- Narrow screens give the actions their own line under the text, so the name never squeezes. -->
    <div class="flex w-full flex-wrap items-center justify-end gap-2 sm:w-auto sm:flex-nowrap">
      <Button
        v-if="switchedOff && can('webhooks:outbound:write')"
        variant="outline"
        class="border-border max-sm:w-full"
        data-outbound-reenable
        :disabled="busy"
        @click="$emit('reenable')"
      >
        {{ $t("dashboard.settings.webhooks.outbound.reenable") }}
      </Button>
      <div data-outbound-actions class="flex items-center gap-1">
        <IconAction
          v-if="can('webhooks:outbound:read')"
          data-outbound-deliveries
          :label="$t('dashboard.settings.webhooks.outbound.deliveries', { name: endpoint.name })"
          @click="$emit('deliveries')"
        >
          <History class="size-5" />
        </IconAction>
        <template v-if="can('webhooks:outbound:write')">
          <IconAction
            data-outbound-test
            :disabled="busy"
            :label="$t('dashboard.settings.webhooks.outbound.test', { name: endpoint.name })"
            @click="$emit('test')"
          >
            <Send class="size-5" />
          </IconAction>
          <IconAction
            data-outbound-rotate
            :label="$t('dashboard.settings.webhooks.outbound.rotate', { name: endpoint.name })"
            @click="$emit('rotate')"
          >
            <Key class="size-5" />
          </IconAction>
          <IconAction
            data-outbound-edit
            :label="$t('dashboard.settings.webhooks.outbound.edit', { name: endpoint.name })"
            @click="$emit('edit')"
          >
            <Edit class="size-5" />
          </IconAction>
          <IconAction
            data-outbound-delete
            :label="$t('dashboard.settings.webhooks.outbound.delete', { name: endpoint.name })"
            @click="$emit('remove')"
          >
            <Trash class="size-5" />
          </IconAction>
        </template>
        <GatedSwitch
          class="ml-2"
          action="webhooks:outbound:write"
          :model-value="endpoint.isEnabled"
          :disabled="busy"
          :aria-label="$t('dashboard.settings.webhooks.outbound.toggle', { name: endpoint.name })"
          @update:model-value="$emit('toggle')"
        />
      </div>
    </div>
  </div>
</template>
