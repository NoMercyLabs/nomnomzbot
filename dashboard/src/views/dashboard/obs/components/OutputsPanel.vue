<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The OBS outputs: stream, recording, replay buffer, virtual camera. Each row shows its state and timecode.
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
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';

import { wholeSeconds } from '../outputFormat';

const props = defineProps<{
  channelId: string | null;
  state: ObsState | null | undefined;
  virtualCam: boolean;
  panelState: PanelState;
  offline: boolean;
}>();

defineEmits<{ retry: [] }>();

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
    :title="$t('dashboard.obs.outputs.title')"
    :state="panel"
    :empty-text="$t('dashboard.obs.outputs.offline')"
    :error-text="$t('dashboard.obs.outputs.loadError')"
    @retry="$emit('retry')"
  >
    <div class="px-1 py-0.5">
      <div data-output="streaming">
        <SummaryRow
          :title="$t('dashboard.obs.outputs.stream')"
          :detail="$t(streamDetail)"
          :meta="streaming ? wholeSeconds(state?.streamTimecode) : undefined"
        >
          <template #trailing>
            <Button
              v-if="!streaming"
              data-act="start"
              :disabled="busy"
              @click="run('streaming', 'start')"
            >
              {{ $t('dashboard.obs.outputs.goLive') }}
            </Button>
            <Button
              v-else
              data-act="stop"
              variant="destructive"
              :disabled="busy"
              @click="confirmStop = true"
            >
              {{ $t('dashboard.obs.outputs.stopStream.action') }}
            </Button>
          </template>
        </SummaryRow>
      </div>

      <div data-output="recording">
        <SummaryRow
          :title="$t('dashboard.obs.outputs.record')"
          :detail="$t(recordDetail)"
          :meta="recording ? wholeSeconds(state?.recordTimecode) : undefined"
        >
          <template #trailing>
            <Button
              v-if="!recording"
              data-act="start"
              variant="outline"
              :disabled="busy"
              @click="run('recording', 'start')"
            >
              {{ $t('dashboard.obs.outputs.start') }}
            </Button>
            <template v-else>
              <Button
                v-if="!paused"
                data-act="pause"
                variant="outline"
                :disabled="busy"
                @click="run('recording', 'pause')"
              >
                {{ $t('dashboard.obs.outputs.pause') }}
              </Button>
              <Button
                v-else
                data-act="resume"
                variant="outline"
                :disabled="busy"
                @click="run('recording', 'resume')"
              >
                {{ $t('dashboard.obs.outputs.resume') }}
              </Button>
              <Button
                data-act="split"
                variant="outline"
                :disabled="busy"
                @click="run('recording', 'split')"
              >
                {{ $t('dashboard.obs.outputs.split') }}
              </Button>
              <Button
                data-act="stop"
                variant="outline"
                :disabled="busy"
                @click="run('recording', 'stop')"
              >
                {{ $t('dashboard.obs.outputs.stop') }}
              </Button>
            </template>
          </template>
        </SummaryRow>
      </div>

      <div data-output="replay-buffer">
        <SummaryRow
          :title="$t('dashboard.obs.outputs.replay')"
          :detail="replay ? $t('dashboard.obs.outputs.active') : $t('dashboard.obs.outputs.inactive')"
        >
          <template #trailing>
            <Button
              v-if="replay"
              data-act="save"
              variant="outline"
              :disabled="busy"
              @click="saveReplay.mutate()"
            >
              {{ $t('dashboard.obs.outputs.saveClip') }}
            </Button>
            <Button
              :data-act="replay ? 'stop' : 'start'"
              variant="outline"
              :disabled="busy"
              @click="run('replay-buffer', replay ? 'stop' : 'start')"
            >
              {{ replay ? $t('dashboard.obs.outputs.stop') : $t('dashboard.obs.outputs.start') }}
            </Button>
          </template>
        </SummaryRow>
      </div>

      <div data-output="virtual-cam">
        <SummaryRow
          :title="$t('dashboard.obs.outputs.virtualCam')"
          :detail="virtualCam ? $t('dashboard.obs.outputs.active') : $t('dashboard.obs.outputs.inactive')"
        >
          <template #trailing>
            <Button
              :data-act="virtualCam ? 'stop' : 'start'"
              variant="outline"
              :disabled="busy"
              @click="run('virtual-cam', virtualCam ? 'stop' : 'start')"
            >
              {{ virtualCam ? $t('dashboard.obs.outputs.stop') : $t('dashboard.obs.outputs.start') }}
            </Button>
          </template>
        </SummaryRow>
      </div>
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
