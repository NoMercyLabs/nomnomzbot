<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Test button of one event type, on a list row and in the editor header. One press fires a sample event
// through the response and opens a dialog with what it presented. Only people who can edit responses see it.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTestEventResponse } from '@/api/eventResponses';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import EventResponseTestResult from './EventResponseTestResult.vue';
import { useEventName } from './useEventName';

const props = defineProps<{ eventType: string }>();

const { t } = useI18n();
const { can } = useCan();
const eventName = useEventName();
const { channelId } = useCurrentChannel();
const test = useTestEventResponse(channelId);

const open = ref(false);

function run(): void {
  open.value = true;
  test.mutate(props.eventType);
}
</script>

<template>
  <template v-if="can('eventresponses:write')">
    <Button
      data-event-test
      type="button"
      variant="outline"
      size="sm"
      :disabled="test.isPending.value"
      @click="run"
    >
      {{ $t('dashboard.eventResponses.test.button') }}
    </Button>
    <Dialog v-model:open="open">
      <DialogContent
        data-event-test-dialog
        :show-close-button="false"
        class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-lg"
      >
        <DialogHeader class="flex-none px-6 pt-6 pb-4">
          <DialogTitle class="text-lg font-semibold">
            {{ $t('dashboard.eventResponses.test.title', { event: eventName(eventType) }) }}
          </DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t('dashboard.eventResponses.test.description') }}
          </DialogDescription>
        </DialogHeader>
        <div class="min-h-0 flex-1 overflow-y-auto px-6 pb-4">
          <p v-if="test.isPending.value" class="flex items-center gap-2 text-sm text-muted-foreground">
            <Spinner />
            {{ $t('dashboard.eventResponses.test.running') }}
          </p>
          <p v-else-if="test.isError.value" role="alert" class="text-sm text-destructive">
            {{ $t('dashboard.eventResponses.test.error') }} {{ errorText(test.error.value, t) }}
          </p>
          <EventResponseTestResult v-else-if="test.data.value" :result="test.data.value" />
        </div>
        <DialogFooter class="flex-none border-t border-border px-6 py-4">
          <DialogClose as-child>
            <Button type="button" variant="outline">{{ $t('dashboard.eventResponses.test.close') }}</Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </template>
</template>
