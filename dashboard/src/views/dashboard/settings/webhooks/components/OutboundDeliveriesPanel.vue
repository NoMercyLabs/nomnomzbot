<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The side panel that holds the delivery log of one outbound webhook.
import type { OutboundWebhook } from '@/api/webhooks';
import SidePanel from '@/components/layout/SidePanel.vue';

import OutboundDeliveriesCard from './OutboundDeliveriesCard.vue';

defineProps<{ endpoint: OutboundWebhook | null }>();

const open = defineModel<boolean>('open', { required: true });
</script>

<template>
  <SidePanel v-model:open="open" :label="$t('dashboard.settings.webhooks.deliveries.title')">
    <OutboundDeliveriesCard :endpoint="endpoint" />
  </SidePanel>
</template>
