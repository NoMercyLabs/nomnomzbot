<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The rarer actions of the Workbench (mockup 5-workbench): turn the pipeline off or on, or delete it.
// The delete confirm names everything that starts the pipeline, because all of it stops running it.
import MenuDots from '~icons/nnz/menu-dots-horizontal';
import { toast } from 'vue-sonner';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useDeletePipeline, useSavePipeline, type Pipeline } from '@/api/pipelines';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import { errorText } from '@/lib/errors';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

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

const saving = useSavePipeline(() => props.channelId);
const removing = useDeletePipeline(() => props.channelId);

function togglePower(): void {
  const { id, name, isEnabled } = props.pipeline;
  const done = isEnabled
    ? 'turnedOff'
    : 'turnedOn';

  saving.mutate({ id, isEnabled: !isEnabled }, {
    onSuccess: () => {
      toast.success(t(`${page}.power.${done}`, { name }));
    },
  });
}

const deleting = ref(false);

const consequences = computed(() =>
  props.sources.map((source: StartSource) =>
    t(`${page}.delete.consequence`, { source: t(`${page}.starts.${source.kind}`, { name: source.name }) })));

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
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button
        data-workbench-more
        type="button"
        variant="ghost"
        size="icon-sm"
        :disabled="!canWrite"
        :aria-label="$t(`${page}.more`)"
        :title="$t(`${page}.more`)"
      >
        <MenuDots aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem data-workbench-power @select="togglePower">
        {{ $t(`${page}.power.${pipeline.isEnabled ? 'off' : 'on'}`) }}
      </DropdownMenuItem>
      <DropdownMenuItem data-workbench-delete variant="destructive" @select="deleting = true">
        {{ $t(`${page}.delete.item`) }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
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
