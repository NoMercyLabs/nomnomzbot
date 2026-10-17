<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Enabled switch saves at once, like every editor's, and sends only the switch, never the unsaved steps.
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useSavePipeline, type Pipeline } from '@/api/pipelines';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';

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

  saving.mutate({ id, isEnabled }, {
    onSuccess: () => {
      toast.success(t(`${page}.power.${done}`, { name }));
    },
  });
}
</script>

<template>
  <LabeledSwitch
    data-workbench-power
    :label="$t(`${page}.power.label`)"
    :help="$t(`${page}.power.help`)"
    :disabled="!canWrite"
    :model-value="pipeline.isEnabled"
    @update:model-value="setPower"
  />
</template>
