<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Enabled switch saves at once, like every editor's, and sends only the switch, never the unsaved steps.
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useSavePipeline, type Pipeline } from '@/api/pipelines';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Badge } from '@/components/ui/badge';

const props = defineProps<{
  channelId: string | null;
  pipeline: Pipeline;
  canWrite: boolean;
}>();

const { t } = useI18n();
const page = 'dashboard.pipelines.workbench';

const saving = useSavePipeline(() => props.channelId);

function setPower(isEnabled: boolean): void {
  const { id, name } = props.pipeline;
  const done = isEnabled
    ? 'turnedOn'
    : 'turnedOff';

  saving.mutate(
    { id, isEnabled },
    {
      onSuccess: () => {
        toast.success(t(`${page}.power.${done}`, { name }));
      },
    },
  );
}
</script>

<template>
  <LabeledSwitch
    v-if="canWrite"
    data-workbench-power
    :label="$t(`${page}.power.label`)"
    :help="$t(`${page}.power.help`)"
    :model-value="pipeline.isEnabled"
    @update:model-value="setPower"
  />
  <Badge v-else data-workbench-power-state variant="outline">{{
    $t(
      pipeline.isEnabled
        ? "dashboard.pipelines.workbench.power.on"
        : "dashboard.pipelines.workbench.power.off",
    )
  }}</Badge>
</template>
