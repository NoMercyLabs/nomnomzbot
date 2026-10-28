<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The OBS outputs: stream, recording, replay buffer, virtual camera, one dense row each with its state and timer.
// One primary button on the panel: "Go live", and only while the stream is off. Stopping the stream is destructive
// and asks first, because it ends the broadcast for every viewer.
import { computed, ref } from 'vue';

import type { ObsState } from '@/api/obs';
import {
  useObsOutputAction,
  useObsSaveReplay,
  type ObsOutput,
  type ObsOutputAction,
} from '@/api/obsOutputs';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import type { PanelState } from '@/composables/usePanelState';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';

import OutputRow from './OutputRow.vue';

const props = defineProps<{
  channelId: string | null;
  state: ObsState | null | undefined;
  virtualCam: boolean;
  streamTimer: string;
  recordTimer: string;
  panelState: PanelState;
  offline: boolean;
}>();

defineEmits<{ retry: [] }>();

// The panel and its dialog make a fragment, so the parent's class (its grid area) goes to the panel by hand.
defineOptions({ inheritAttrs: false });

const { can } = useCan();
const canBroadcast = computed<boolean>(() => can('obs:control:broadcast'));
const action = useObsOutputAction(() => props.channelId);
const saveReplay = useObsSaveReplay(() => props.channelId);
const confirmStop = ref(false);

const streaming = computed<boolean>(() => props.state?.streaming === true);
const recording = computed<boolean>(() => props.state?.recording === true);
const paused = computed<boolean>(() => props.state?.recordPaused === true);
const replay = computed<boolean>(() => props.state?.replayBufferActive === true);
const busy = computed<boolean>(() => action.isPending.value || saveReplay.isPending.value);

const panel = computed<PanelState>(() => {
  if (props.offline)
    return 'empty';

  return props.panelState === 'ready' && !props.state
    ? 'loading'
    : props.panelState;
});

const streamDetail = computed<string>(() => {
  if (!streaming.value)
    return 'dashboard.obs.outputs.offAir';

  return 'dashboard.obs.outputs.live';
});

const recordDetail = computed<string>(() => {
  if (!recording.value)
    return 'dashboard.obs.outputs.notRecording';

  return paused.value
    ? 'dashboard.obs.outputs.paused'
    : 'dashboard.obs.outputs.recording';
});

function run(output: ObsOutput, what: ObsOutputAction): void {
  action.mutate({ output, action: what });
}

async function stopStream(): Promise<void> {
  await action.mutateAsync({ output: 'streaming', action: 'stop' });
  confirmStop.value = false;
}
</script>

<template>
  <PanelCard
    v-bind="$attrs"
    :title="$t('dashboard.obs.outputs.title')"
    :state="panel"
    :empty-text="$t('dashboard.obs.outputs.offline')"
    :error-text="$t('dashboard.obs.outputs.loadError')"
    @retry="$emit('retry')"
  >
    <div class="divide-y divide-border">
      <OutputRow
        data-output="streaming"
        :name="$t('dashboard.obs.outputs.stream')"
        :state="$t(streamDetail)"
        :on="streaming"
        :timer="streamTimer"
      >
        <ReadOnlyScope
          class="flex items-center gap-2"
          :read-only="!canBroadcast"
          :reason="$t('dashboard.obs.needsBroadcast')"
        >
          <Button
            v-if="!streaming"
            data-act="start"
            size="sm"
            :disabled="busy"
            @click="run('streaming', 'start')"
          >
            {{ $t("dashboard.obs.outputs.goLive") }}
          </Button>
          <Button
            v-else
            data-act="stop"
            size="sm"
            variant="destructive"
            :disabled="busy"
            @click="confirmStop = true"
          >
            {{ $t("dashboard.obs.outputs.stopStream.action") }}
          </Button>
        </ReadOnlyScope>
      </OutputRow>

      <OutputRow
        data-output="recording"
        :name="$t('dashboard.obs.outputs.record')"
        :state="$t(recordDetail)"
        :on="recording"
        :timer="recordTimer"
      >
        <ReadOnlyScope
          class="flex items-center gap-2"
          :read-only="!canBroadcast"
          :reason="$t('dashboard.obs.needsBroadcast')"
        >
          <template v-if="recording">
            <Button
              v-if="!paused"
              data-act="pause"
              size="sm"
              variant="ghost"
              :disabled="busy"
              @click="run('recording', 'pause')"
            >
              {{ $t("dashboard.obs.outputs.pause") }}
            </Button>
            <Button
              v-else
              data-act="resume"
              size="sm"
              variant="ghost"
              :disabled="busy"
              @click="run('recording', 'resume')"
            >
              {{ $t("dashboard.obs.outputs.resume") }}
            </Button>
            <Button
              data-act="split"
              size="sm"
              variant="ghost"
              :disabled="busy"
              @click="run('recording', 'split')"
            >
              {{ $t("dashboard.obs.outputs.split") }}
            </Button>
          </template>
          <Button
            :data-act="recording ? 'stop' : 'start'"
            size="sm"
            variant="outline"
            :disabled="busy"
            @click="run('recording', recording ? 'stop' : 'start')"
          >
            {{ recording ? $t("dashboard.obs.outputs.stop") : $t("dashboard.obs.outputs.start") }}
          </Button>
        </ReadOnlyScope>
      </OutputRow>

      <OutputRow
        data-output="replay-buffer"
        :name="$t('dashboard.obs.outputs.replay')"
        :state="replay ? $t('dashboard.obs.outputs.active') : $t('dashboard.obs.outputs.inactive')"
        :on="replay"
      >
        <Button
          v-if="replay"
          data-act="save"
          size="sm"
          variant="ghost"
          :disabled="busy"
          @click="saveReplay.mutate()"
        >
          {{ $t("dashboard.obs.outputs.saveClip") }}
        </Button>
        <Button
          :data-act="replay ? 'stop' : 'start'"
          size="sm"
          variant="outline"
          :disabled="busy"
          @click="run('replay-buffer', replay ? 'stop' : 'start')"
        >
          {{ replay ? $t("dashboard.obs.outputs.stop") : $t("dashboard.obs.outputs.start") }}
        </Button>
      </OutputRow>

      <OutputRow
        data-output="virtual-cam"
        :name="$t('dashboard.obs.outputs.virtualCam')"
        :state="
          virtualCam ? $t('dashboard.obs.outputs.active') : $t('dashboard.obs.outputs.inactive')
        "
        :on="virtualCam"
      >
        <Button
          :data-act="virtualCam ? 'stop' : 'start'"
          size="sm"
          variant="outline"
          :disabled="busy"
          @click="run('virtual-cam', virtualCam ? 'stop' : 'start')"
        >
          {{ virtualCam ? $t("dashboard.obs.outputs.stop") : $t("dashboard.obs.outputs.start") }}
        </Button>
      </OutputRow>
    </div>
    <div v-if="$slots.default" class="border-t border-border">
      <slot />
    </div>
  </PanelCard>

  <ConfirmDialog
    v-model:open="confirmStop"
    :title="$t('dashboard.obs.outputs.stopStream.title')"
    :description="$t('dashboard.obs.outputs.stopStream.description')"
    :consequences="[
      $t('dashboard.obs.outputs.stopStream.viewers'),
      $t('dashboard.obs.outputs.stopStream.recording'),
    ]"
    :action="$t('dashboard.obs.outputs.stopStream.action')"
    :pending="action.isPending.value"
    @confirm="stopStream()"
  />
</template>
