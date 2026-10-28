<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// After a delete the channel is only hidden until the bot removes it for good. This notice says until when and
// offers the way back, for as long as this page stays open.
import { computed } from 'vue';

import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useTime } from '@/composables/useTime';

const props = defineProps<{
  name?: string;
  permanentAfter: string;
  busy: boolean;
}>();

defineEmits<{ restore: [] }>();

const { can } = useCan();

const time = useTime();
const until = computed(() =>
  time.dateTime(props.permanentAfter));
</script>

<template>
  <div data-slot="deleted-channel" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <p class="text-sm font-medium">
        {{ name ?? $t('dashboard.settings.channelManagement.deleted.title') }}
      </p>
      <p class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.channelManagement.deleted.help', { when: until }) }}
      </p>
    </div>
    <Button v-if="can('setup:write')"
            data-restore
            variant="outline"
            :disabled="busy"
            @click="$emit('restore')"
    >
      {{ $t('dashboard.settings.channelManagement.deleted.restore') }}
    </Button>
  </div>
</template>
