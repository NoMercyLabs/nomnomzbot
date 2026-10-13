<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One script in full: name and switch, how the last run went, what it is, the powers its manifest declares,
// and Delete. Delete reads the blast list first and asks; nothing is deleted before the owner confirms.
// Powers are plain text here: the bot has no per-power switch yet. Edit code and Run test come with the editor.
import { ArrowLeft, Trash2 } from '@lucide/vue'
import { useTimeAgoIntl } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  fetchBlastRadius,
  useDeleteCodeScript,
  useScriptDetail,
  useSetScriptEnabled,
  type BlastRadius,
  type CodeScriptSummary,
} from '@/api/codeScripts'
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { usePanelState } from '@/composables/usePanelState'

const props = defineProps<{ scriptId: string; summary?: CodeScriptSummary }>()
const emit = defineEmits<{ deleted: []; back: [] }>()

const { t, te, locale } = useI18n()
const detail = useScriptDetail(() => props.scriptId)
const setEnabled = useSetScriptEnabled()
const remove = useDeleteCodeScript()

const state = usePanelState(detail, () => false)
const script = computed(() => detail.data.value)
// The detail answer carries no run facts; the list row of the same script does.
const ago = useTimeAgoIntl(() => props.summary?.lastRanAt ?? new Date(0), { locale: locale.value })

const runTone: ComputedRef<StatusTone> = computed<StatusTone>(() => {
  if (props.summary?.lastRuntimeError) return 'bad'
  return props.summary?.lastRanAt ? 'ok' : 'off'
})
const runTitle: ComputedRef<string> = computed<string>(() => {
  if (props.summary?.lastRuntimeError) return t('dashboard.scripts.detail.runFailed')
  if (!props.summary?.lastRanAt) return t('dashboard.scripts.list.neverRun')
  return t('dashboard.scripts.detail.runWorked', { when: ago.value })
})

const facts: ComputedRef<KeyValueItem[]> = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.scripts.detail.version'), value: script.value?.currentVersion?.version },
  { label: t('dashboard.scripts.detail.language'), value: script.value?.language },
])
const powers: ComputedRef<string[]> = computed<string[]>(
  () => script.value?.currentVersion?.declaredCapabilities ?? [],
)

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ id: props.scriptId, isEnabled })
}

const confirmOpen: Ref<boolean> = ref(false)
const consequences: Ref<string[]> = ref([])
const deleteError: Ref<string | undefined> = ref(undefined)

// A category key is a resource key; a key this page has no words for shows its last part.
function categoryLabel(key: string): string {
  return te(key) ? t(key) : (key.split('.').pop() ?? key)
}

function blastLines(blast: BlastRadius): string[] {
  const lines: string[] = blast.categories
    .filter((category) => Number(category.count) > 0)
    .map((category) =>
      t(
        blast.isMinimum ? 'dashboard.scripts.delete.lineAtLeast' : 'dashboard.scripts.delete.line',
        {
          count: Number(category.count),
          what: categoryLabel(category.categoryKey),
          names: category.sample.join(', '),
        },
      ),
    )
  return lines.length > 0 ? lines : [t('dashboard.scripts.delete.nothing')]
}

async function askDelete(): Promise<void> {
  deleteError.value = undefined
  try {
    consequences.value = blastLines(await fetchBlastRadius(props.scriptId))
    confirmOpen.value = true
  } catch (failure) {
    deleteError.value = (failure as Error).message
  }
}

function confirmDelete(): void {
  remove.mutate(props.scriptId, {
    onSuccess: (): void => {
      confirmOpen.value = false
      emit('deleted')
    },
    onError: (failure: Error): void => {
      confirmOpen.value = false
      deleteError.value = failure.message
    },
  })
}
</script>

<template>
  <PanelCard
    data-slot="script-detail"
    class="flex-1"
    tier="lead"
    pad
    :title="script?.name ?? $t('dashboard.scripts.detail.title')"
    :state="state"
    :empty-text="$t('dashboard.scripts.detail.notFound', { id: scriptId })"
    :error-text="detail.error.value?.message"
    @retry="detail.refetch()"
  >
    <template v-if="script" #actions>
      <Button
        variant="ghost"
        size="icon-sm"
        class="md:hidden"
        :aria-label="$t('dashboard.scripts.detail.back')"
        @click="$emit('back')"
      >
        <ArrowLeft />
      </Button>
      <label class="flex items-center gap-2 text-sm text-muted-foreground">
        {{ $t(script.isEnabled ? 'dashboard.scripts.detail.on' : 'dashboard.scripts.detail.off') }}
        <Switch
          :model-value="script.isEnabled"
          :aria-label="$t('dashboard.scripts.detail.switchLabel')"
          @update:model-value="toggle(Boolean($event))"
        />
      </label>
    </template>

    <div v-if="script" class="grid gap-5 text-sm">
      <div class="grid gap-1">
        <StatusBadge :tone="runTone" :label="runTitle" class="font-medium" />
        <p v-if="summary?.lastRuntimeError" class="text-muted-foreground wrap-break-word">
          {{ summary.lastRuntimeError }}
        </p>
        <p class="text-muted-foreground">
          {{ script.description || $t('dashboard.scripts.detail.noDescription') }}
        </p>
      </div>

      <KeyValueList :items="facts" />

      <section class="grid gap-2" data-slot="script-powers">
        <h3 class="font-semibold">{{ $t('dashboard.scripts.detail.powers.title') }}</h3>
        <p class="text-muted-foreground">{{ $t('dashboard.scripts.detail.powers.note') }}</p>
        <ul v-if="powers.length > 0" class="grid gap-1">
          <li v-for="power in powers" :key="power" class="font-mono">{{ power }}</li>
        </ul>
        <p v-else class="text-muted-foreground">{{ $t('dashboard.scripts.detail.powers.none') }}</p>
      </section>

      <section class="grid gap-1">
        <h3 class="font-semibold">{{ $t('dashboard.scripts.detail.settings') }}</h3>
        <p class="text-muted-foreground">{{ $t('dashboard.scripts.detail.noSettings') }}</p>
      </section>

      <p v-if="deleteError" role="alert" class="text-destructive">{{ deleteError }}</p>
    </div>

    <template v-if="script" #footer>
      <span class="text-xs text-muted-foreground">{{ $t('dashboard.scripts.detail.limits') }}</span>
      <Button class="ml-auto" variant="destructive" size="sm" @click="askDelete">
        <Trash2 aria-hidden="true" />
        {{ $t('dashboard.scripts.detail.delete') }}
      </Button>
    </template>
  </PanelCard>

  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.scripts.delete.title', { name: script?.name ?? '' })"
    :description="$t('dashboard.scripts.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.scripts.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
