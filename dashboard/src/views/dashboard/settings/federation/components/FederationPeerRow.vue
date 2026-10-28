<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A choice that names one peer instance (not any trusted peer): the kind of data, the way it runs and the peer.
// Turning it off returns that flow to default-deny for the peer.
import Trash from '~icons/nnz/trash';

import type { FederationOptIn } from '@/api/federation';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

defineProps<{
  optIn: FederationOptIn;
  busy: boolean;
  canDisable: boolean;
}>();

defineEmits<{ disable: [] }>();
</script>

<template>
  <div :data-peer-opt-in="optIn.id" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <span class="flex flex-wrap items-center gap-2">
        <span class="text-sm font-medium wrap-break-word">
          {{ $t(`dashboard.settings.federation.type.${optIn.optInType}.title`) }}
        </span>
        <Badge variant="outline">{{ $t(`dashboard.settings.federation.choice.${optIn.direction}`) }}</Badge>
      </span>
      <span class="block font-mono text-sm break-all text-muted-foreground">{{ optIn.peerId }}</span>
    </div>
    <Button
      v-if="canDisable"
      variant="ghost"
      size="icon"
      data-peer-opt-in-disable
      :disabled="busy"
      :aria-label="$t('dashboard.settings.federation.peers.disable')"
      @click="$emit('disable')"
    >
      <Trash class="size-5" />
    </Button>
  </div>
</template>
