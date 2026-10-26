<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Delete asks first, and the confirm names everything that starts the pipeline, because all of it stops running it.
import { toast } from 'vue-sonner';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useDeletePipeline, type Pipeline } from '@/api/pipelines';
import EditorFooter from '@/components/layout/EditorFooter.vue';
import { errorText } from '@/lib/errors';

import type { StartSource } from './stepWords';

const props = defineProps<{
  channelId: string | null;
  pipeline: Pipeline;
  canWrite: boolean;
  sources: StartSource[];
}>();

const emit = defineEmits<{ deleted: [] }>();

const { t } = useI18n();
const page = 'dashboard.pipelines.workbench';

const removing = useDeletePipeline(() => props.channelId);
const deleting = ref(false);

const consequences = computed(() =>
  props.sources.map((source) =>
    t(`${page}.delete.consequence`, {
      source: t(`${page}.starts.${source.kind}`, { name: source.name }),
    }),
  ),
);

async function remove(): Promise<void> {
  const { id, name } = props.pipeline;

  try {
    await removing.mutateAsync(id);
    deleting.value = false;
    toast.success(t(`${page}.delete.done`, { name }));
    emit('deleted');
  } catch (error: unknown) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t(`${page}.delete.failed`));
  }
}
</script>

<template>
  <EditorFooter
    v-if="canWrite"
    v-model:confirm-open="deleting"
    :delete-label="$t(`${page}.delete.item`)"
    :confirm-title="$t(`${page}.delete.title`, { name: pipeline.name })"
    :confirm-description="$t(`${page}.delete.description`)"
    :confirm-action="$t(`${page}.delete.action`)"
    :consequences="consequences"
    :deleting="removing.isPending.value"
    @ask-delete="deleting = true"
    @confirm-delete="remove"
  />
</template>
