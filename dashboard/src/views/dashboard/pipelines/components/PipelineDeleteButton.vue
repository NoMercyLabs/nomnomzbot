<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Delete asks first, and the confirm names everything that starts the pipeline, because all of it stops running it.
import Trash2 from '~icons/nnz/trash';
import { toast } from 'vue-sonner';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useDeletePipeline, type Pipeline } from '@/api/pipelines';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
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
  <Button
    v-if="canWrite"
    data-workbench-delete
    type="button"
    variant="outline"
    size="sm"
    @click="deleting = true"
  >
    <Trash2 aria-hidden="true" />
    {{ $t(`${page}.delete.item`) }}
  </Button>
  <ConfirmDialog
    v-model:open="deleting"
    :title="$t(`${page}.delete.title`, { name: pipeline.name })"
    :description="$t(`${page}.delete.description`)"
    :consequences="consequences"
    :action="$t(`${page}.delete.action`)"
    :pending="removing.isPending.value"
    @confirm="remove"
  />
</template>
