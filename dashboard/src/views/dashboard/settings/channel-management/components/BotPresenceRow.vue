<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Whether the bot is in the channel and the one action that changes it. Joining is the main action while the bot
// is out; leaving is a quiet outline action, because it stops the bot answering chat and listening to events.
import { computed } from 'vue';

import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';

const props = defineProps<{
  joinedAt: string | null | undefined;
  busy: boolean;
}>();

defineEmits<{ join: []; leave: [] }>();

const { can } = useCan();

const joined = computed(() =>
  props.joinedAt !== null && props.joinedAt !== undefined);

const since = computed(() =>
  joined.value
    ? new Date(props.joinedAt as string).toLocaleString()
    : '');
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
