<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// After a delete the channel is only hidden until the bot removes it for good. This notice says until when and
// offers the way back, for as long as this page stays open.
import { computed } from 'vue';

import { Button } from '@/components/ui/button';

const props = defineProps<{
  permanentAfter: string;
  busy: boolean;
}>();

defineEmits<{ restore: [] }>();

const until = computed(() =>
  new Date(props.permanentAfter).toLocaleString());
</script>

<template>
  <div data-slot="deleted-channel" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
    <div class="min-w-0 flex-1">
      <p class="text-sm font-medium">{{ $t('dashboard.settings.channelManagement.deleted.title') }}</p>
      <p class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.channelManagement.deleted.help', { when: until }) }}
      </p>
    </div>
    <Button data-restore variant="outline" :disabled="busy" @click="$emit('restore')">
      {{ $t('dashboard.settings.channelManagement.deleted.restore') }}
    </Button>
  </div>
</template>
