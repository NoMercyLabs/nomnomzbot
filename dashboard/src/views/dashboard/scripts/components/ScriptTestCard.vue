<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Test one script: pick the event to test with (or none) and run it. A test run is a dry run:
// nothing it does reaches chat. The sample events are the bot's one shared list. The bot does not yet say
// which events a script handles, so the choice keeps the events that hand over a value the script's own
// code reads; a script that reads nothing from its trigger is tested with no event.
import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import Play from '~icons/nnz/play';

import { fetchTestTriggers } from '@/api/codeScripts';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import type { TestTrigger } from '@/composables/useEditorBridge';
import { useCan } from '@/composables/useCan';

import { eventsFor, readInputs } from './scriptInputs';
import type { SessionRun } from './sessionRun';
import { useScriptTester } from './useScriptTester';

const NO_EVENT = 'none';

const props = defineProps<{
  scriptId: string;
  scriptName: string;
  source: string;
}>();
const emit = defineEmits<{ ran: [run: Omit<SessionRun, 'id'>] }>();

const { can } = useCan();
const tester = useScriptTester(() => props.scriptId);
const triggers: UseQueryReturnType<TestTrigger[], Error> = useQuery({
  queryKey: ['code-scripts', 'test-triggers'],
  queryFn: fetchTestTriggers,
});

const ownEvents = computed<TestTrigger[]>(() =>
  eventsFor(triggers.data.value ?? [], readInputs(props.source).variables),
);
const picked = ref<string>(NO_EVENT);
const chosen = computed<TestTrigger | null>(
  () => ownEvents.value.find((sample) => sample.id === picked.value) ?? null,
);

async function run(): Promise<void> {
  const done = await tester.run(chosen.value);

  if (!done) return;

  emit('ran', {
    scriptId: props.scriptId,
    scriptName: props.scriptName,
    at: new Date(),
    trigger: done.trigger,
    result: done.result,
  });
}
</script>

<template>
  <PanelCard
    data-slot="script-test"
    class="shrink-0"
    pad
    :title="$t('dashboard.scripts.test.title')"
    :description="$t('dashboard.scripts.test.note')"
  >
    <div class="grid gap-4 text-sm">
      <div v-if="can('code:script:author')" class="flex flex-wrap items-end gap-3">
        <div v-if="ownEvents.length > 0" class="grid min-w-0 flex-1 gap-2">
          <Label for="script-test-event">{{ $t('dashboard.scripts.test.event') }}</Label>
          <Select v-model="picked">
            <SelectTrigger id="script-test-event" class="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem :value="NO_EVENT">{{ $t('dashboard.scripts.test.noEvent') }}</SelectItem>
              <SelectItem v-for="sample in ownEvents" :key="sample.id" :value="sample.id">
                {{ sample.label }}
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <Button
          variant="outline"
          :disabled="tester.running.value || tester.limited.value"
          @click="run"
        >
          <Play aria-hidden="true" />
          {{ $t('dashboard.scripts.test.run') }}
        </Button>
      </div>
      <span v-if="tester.limited.value" role="status" class="text-muted-foreground">
        {{ $t('dashboard.scripts.test.limited') }}
      </span>

      <div v-if="tester.last.value" data-slot="script-test-result" class="grid gap-1">
        <StatusBadge
          :tone="tester.last.value.success ? 'ok' : 'bad'"
          :label="
            $t(
              tester.last.value.success
                ? 'dashboard.scripts.test.worked'
                : 'dashboard.scripts.test.failed',
              { ms: tester.last.value.durationMs },
            )
          "
          class="font-medium"
        />
        <p v-if="tester.last.value.error" class="text-muted-foreground wrap-break-word">
          {{ tester.last.value.error }}
        </p>
        <p
          v-for="(line, index) in tester.last.value.chatOutput"
          :key="index"
          class="text-muted-foreground"
        >
          {{ line }}
        </p>
      </div>
      <p v-if="tester.failure.value" role="alert" class="text-destructive">
        {{ tester.failure.value }}
      </p>
    </div>
  </PanelCard>
</template>
