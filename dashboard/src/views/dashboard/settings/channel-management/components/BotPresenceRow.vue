<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Whether the bot is in the channel and the one action that changes it. Joining is the main action while the bot
// is out; leaving is a quiet outline action, because it stops the bot answering chat and listening to events.
import { computed } from 'vue';

import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useFormattedTime } from '@/composables/useTime';

const props = defineProps<{
  joinedAt: string | null | undefined;
  busy: boolean;
}>();

defineEmits<{ join: []; leave: [] }>();

const { can } = useCan();

const joined = computed(() =>
  props.joinedAt !== null && props.joinedAt !== undefined);

const since = useFormattedTime(() => props.joinedAt, 'dateTime');
</script>

<template>
  <div data-slot="bot-presence" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <StatusBadge
        :tone="joined ? 'ok' : 'off'"
        :label="joined ? $t('dashboard.settings.channelManagement.presence.in') : $t('dashboard.settings.channelManagement.presence.out')"
      />
      <p class="text-sm text-muted-foreground">
        {{
          joined
            ? $t('dashboard.settings.channelManagement.presence.since', { when: since })
            : $t('dashboard.settings.channelManagement.presence.outHelp')
        }}
      </p>
    </div>
    <Button v-if="joined && can('setup:write')"
            data-leave
            variant="outline"
            :disabled="busy"
            @click="$emit('leave')"
    >
      {{ $t('dashboard.settings.channelManagement.presence.leave') }}
    </Button>
    <Button v-else-if="can('setup:write')" data-join :disabled="busy" @click="$emit('join')">
      {{ $t('dashboard.settings.channelManagement.presence.join') }}
    </Button>
  </div>
</template>
