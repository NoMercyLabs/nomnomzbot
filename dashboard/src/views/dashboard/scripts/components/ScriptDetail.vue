<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One script in full. The header (back, name, switch, Edit code) stays in view; only the body scrolls.
// The body: how the last run went, what runs the script, what it can do, a test with its own card, and Delete.
// Edit code opens the code in the editor; the code is never shown as a locked block here.
// Delete reads the blast list first and asks; nothing is deleted before the owner confirms.
// Powers are plain text here: the bot has no per-power switch yet. The bot does not yet declare settings
// for a script, so there is no settings section until it does.
import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Copy from '~icons/nnz/file-copy';

import {
  fetchBlastRadius,
  useCreateCodeScript,
  useDeleteCodeScript,
  useScriptDetail,
  useSetScriptEnabled,
  type BlastRadius,
  type CodeScriptSummary,
} from '@/api/codeScripts';
import BackLink from '@/components/layout/BackLink.vue';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import DetailPane from '@/components/layout/DetailPane.vue';
import EditorFooter from '@/components/layout/EditorFooter.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { errorText } from '@/lib/errors';
import { humanize } from '@/lib/text';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { usePanelState } from '@/composables/usePanelState';
import { useFormattedTime } from '@/composables/useTime';

import { blastLines } from './blastLines';
import { powerLabel } from './powerLabel';
import { inputLines, readInputs } from './scriptInputs';
import ScriptTestCard from './ScriptTestCard.vue';
import type { SessionRun } from './sessionRun';
import { useScriptEditor } from './useScriptEditor';

const props = defineProps<{ scriptId: string; summary?: CodeScriptSummary }>();
const emit = defineEmits<{
  deleted: [];
  duplicated: [id: string | null];
  back: [];
  ran: [run: Omit<SessionRun, 'id'>];
}>();

const { t, te } = useI18n();
const { can } = useCan();
const detail = useScriptDetail(() => props.scriptId);
const setEnabled = useSetScriptEnabled();
const remove = useDeleteCodeScript();
const create = useCreateCodeScript();
const editor = useScriptEditor(() => props.scriptId);
const blast: UseQueryReturnType<BlastRadius, Error> = useQuery({
  queryKey: computed(() => ['code-scripts', 'blast', props.scriptId]),
  queryFn: () => fetchBlastRadius(props.scriptId),
});

const state = usePanelState(detail, () => false);
const script = computed(() => detail.data.value);
// The detail answer carries no run facts; the list row of the same script does.
const ago = useFormattedTime(() => props.summary?.lastRanAt, 'relative', { live: true });

const runTone = computed<StatusTone>(() => {
  if (props.summary?.lastRuntimeError)
    return 'bad';

  return props.summary?.lastRanAt
    ? 'ok'
    : 'off';
});
const runTitle = computed<string>(() => {
  if (props.summary?.lastRuntimeError)
    return t('dashboard.scripts.detail.runFailed');

  if (!props.summary?.lastRanAt)
    return t('dashboard.scripts.list.neverRun');

  return t('dashboard.scripts.detail.runWorked', { when: ago.value });
});

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.scripts.detail.version'), value: script.value?.currentVersion?.version },
  { label: t('dashboard.scripts.detail.language'), value: humanize(script.value?.language ?? '') },
]);
const source = computed<string>(() => script.value?.currentVersion?.sourceCode ?? '');
const reads = computed<string[]>(() => inputLines(readInputs(source.value), t, te));
const powers = computed<string[]>(() =>
  (script.value?.currentVersion?.declaredCapabilities ?? []).map((key) => powerLabel(key, t, te)),
);
// The script's own saved versions are part of the delete list, but they do not run the script.
const OWN_VERSIONS = 'blast_radius_category_code_script_versions';
const usedBy = computed<string[]>(() =>
  blast.data.value
    ? blastLines(
        {
          ...blast.data.value,
          categories: blast.data.value.categories.filter(
            (category) => category.categoryKey !== OWN_VERSIONS,
          ),
        },
        t,
        te,
      )
    : [],
);
const problems = computed<string[]>(() =>
  (script.value?.currentVersion?.validationErrors ?? []).map((problem) =>
    problem.line == null
      ? problem.message
      : t('dashboard.scripts.detail.problems.errorLine', {
          line: Number(problem.line),
          message: problem.message,
        }),
  ),
);

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ id: props.scriptId, isEnabled });
}

const confirmOpen = ref(false);
const consequences = ref<string[]>([]);
const deleteError = ref<string | undefined>(undefined);

// The copy holds the code the script runs now. It is a script of its own: it starts switched off and unshared.
async function copyScript(): Promise<void> {
  const source = script.value?.currentVersion?.sourceCode;

  if (source === undefined) return;

  deleteError.value = undefined;

  try {
    emit(
      'duplicated',
      await create.mutateAsync({
        name: t('dashboard.scripts.detail.copyName', { name: script.value?.name ?? '' }),
        description: script.value?.description,
        sourceCode: source,
      }),
    );
  } catch (failure) {
    deleteError.value = errorText(failure, t);
  }
}

async function askDelete(): Promise<void> {
  deleteError.value = undefined;

  try {
    const lines = blastLines(await fetchBlastRadius(props.scriptId), t, te);

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.scripts.delete.nothing')];

    confirmOpen.value = true;
  } catch (failure) {
    deleteError.value = errorText(failure, t);
  }
}

function confirmDelete(): void {
  remove.mutate(props.scriptId, {
    onSuccess: (): void => {
      confirmOpen.value = false;
      emit('deleted');
    },
    onError: (failure: Error): void => {
      confirmOpen.value = false;
      deleteError.value = errorText(failure, t);
    },
  });
}
</script>

<template>
  <PanelCard
    v-if="!script"
    data-slot="script-detail"
    class="flex-1"
    tier="lead"
    pad
    :title="$t('dashboard.scripts.detail.title')"
    :state="state"
    :empty-text="$t('dashboard.scripts.detail.notFound')"
    :error-text="detail.error.value ? errorText(detail.error.value, t) : undefined"
    @retry="detail.refetch()"
  />

  <DetailPane
    v-else
    data-slot="script-detail"
    class="max-md:flex-none max-md:**:data-[slot=detail-pane-body]:flex-none max-md:**:data-[slot=detail-pane-body]:overflow-visible"
  >
    <template #header>
      <BackLink
        data-page-inset
        data-slot="script-back"
        class="mb-2 md:hidden"
        :label="$t('dashboard.scripts.detail.back')"
        @click="$emit('back')"
      />
      <div data-page-inset class="flex flex-wrap items-center gap-3">
        <h2 class="min-w-0 truncate text-lg font-semibold">{{ script.name }}</h2>
        <div class="ml-auto flex flex-wrap items-center gap-3">
          <label v-if="can('code:script:author')" class="flex items-center gap-2 text-sm text-muted-foreground">
            {{
              $t(script.isEnabled ? 'dashboard.scripts.detail.on' : 'dashboard.scripts.detail.off')
            }}
            <Switch
              :model-value="script.isEnabled"
              :aria-label="$t('dashboard.scripts.detail.switchLabel')"
              @update:model-value="toggle(Boolean($event))"
            />
          </label>
          <Button v-if="can('code:script:author')" :disabled="editor.opening.value" @click="editor.open">
            {{ $t('dashboard.scripts.detail.edit') }}
          </Button>
        </div>
      </div>
    </template>

    <PanelCard class="shrink-0" tier="lead" pad :title="$t('dashboard.scripts.detail.title')">
      <div class="grid gap-5 text-sm">
        <div class="grid gap-1">
          <StatusBadge :tone="runTone" :label="runTitle" class="font-medium" />
          <p v-if="summary?.lastRuntimeError" class="text-muted-foreground wrap-break-word">
            {{ summary.lastRuntimeError }}
          </p>
          <p class="text-muted-foreground">
            {{ script.description || $t('dashboard.scripts.detail.noDescription') }}
          </p>
        </div>

        <ul v-if="problems.length > 0" data-slot="script-problems" class="grid gap-1 text-destructive">
          <li v-for="problem in problems" :key="problem">{{ problem }}</li>
        </ul>
        <p v-if="editor.missing.value" role="alert" class="text-destructive">
          {{ $t('dashboard.scripts.detail.notFound') }}
        </p>

        <section class="grid gap-2" data-slot="script-used-by">
          <h3 class="font-semibold">{{ $t('dashboard.scripts.detail.usedBy.title') }}</h3>
          <ul v-if="usedBy.length > 0" class="grid gap-1">
            <li v-for="line in usedBy" :key="line">{{ line }}</li>
          </ul>
          <p v-else-if="blast.isSuccess.value" class="text-muted-foreground">
            {{ $t('dashboard.scripts.detail.usedBy.none') }}
          </p>
        </section>

        <section v-if="reads.length > 0" class="grid gap-2" data-slot="script-reads">
          <h3 class="font-semibold">{{ $t('dashboard.scripts.detail.reads.title') }}</h3>
          <ul class="grid gap-1">
            <li v-for="line in reads" :key="line">{{ line }}</li>
          </ul>
        </section>

        <section class="grid gap-2" data-slot="script-powers">
          <h3 class="font-semibold">{{ $t('dashboard.scripts.detail.powers.title') }}</h3>
          <ul v-if="powers.length > 0" class="grid gap-1">
            <li v-for="power in powers" :key="power">{{ power }}</li>
          </ul>
          <p v-else class="text-muted-foreground">
            {{ $t('dashboard.scripts.detail.powers.none') }}
          </p>
        </section>

        <KeyValueList :items="facts" />

        <p v-if="deleteError" role="alert" class="text-destructive">{{ deleteError }}</p>
      </div>
    </PanelCard>

    <ScriptTestCard
      :script-id="scriptId"
      :script-name="script.name"
      :source="source"
      @ran="emit('ran', $event)"
    />

    <div data-page-inset
         data-slot="script-footer"
         class="mt-2 flex flex-none border-t pt-4"
    >
      <EditorFooter
        v-model:confirm-open="confirmOpen"
        :delete-label="can('code:script:author') ? $t('dashboard.scripts.detail.delete') : undefined"
        :unsaved="$t('dashboard.scripts.detail.limits')"
        :confirm-title="$t('dashboard.scripts.delete.title', { name: script?.name ?? '' })"
        :confirm-description="$t('dashboard.scripts.delete.description')"
        :confirm-action="$t('dashboard.scripts.delete.action')"
        :consequences="consequences"
        :deleting="remove.isPending.value"
        @ask-delete="askDelete"
        @confirm-delete="confirmDelete"
      >
        <template #extra>
          <Button
            v-if="can('code:script:author')"
            data-slot="script-duplicate"
            variant="outline"
            size="sm"
            :disabled="create.isPending.value || !script?.currentVersion"
            @click="copyScript"
          >
            <Copy aria-hidden="true" />
            {{ $t('dashboard.scripts.detail.duplicate') }}
          </Button>
        </template>
      </EditorFooter>
    </div>
  </DetailPane>

  <CodeEditor
    v-if="editor.session.value"
    :title="script?.name ?? ''"
    :files="editor.session.value.project.files"
    :entry="editor.session.value.project.manifest.entry"
    :sdk-types="editor.session.value.sdkTypes ?? ''"
    :sdk-types-unavailable="editor.session.value.sdkTypes === null"
    :save="editor.save"
    @close="editor.close"
  />
</template>
