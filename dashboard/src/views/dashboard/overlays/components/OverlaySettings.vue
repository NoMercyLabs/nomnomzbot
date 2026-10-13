<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The settings form of one overlay, built from the bot's settings schema (12-overlays.html:304-313, 360-361).
// The form keeps its own unsaved copy. It takes the saved values again only for a new overlay or after a
// successful save, so a refused save leaves the change in place and Save stays on.
// "Changed" means the value differs from the default; Save is on when the form differs from what is saved.
// Without schema fields the saved keys show read-only.
import Save from '~icons/nnz/floppy-disk'
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  useSaveWidgetSettings,
  useWidgetSchema,
  type Widget,
  type WidgetSettingsField,
} from '@/api/widgets'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import OverlaySettingField from './OverlaySettingField.vue'
import { humanize, sameValue, useSettingText } from './settingField'

type Values = Record<string, unknown>
type SaveResult = 'none' | 'saved' | 'failed'

const props = defineProps<{ widget: Widget }>()

const { t } = useI18n()
const text = useSettingText()
const { channelId } = useCurrentChannel()
const schema = useWidgetSchema(channelId, () =>
  props.widget.id)
const save = useSaveWidgetSettings(channelId)

const fields: ComputedRef<WidgetSettingsField[]> = computed<WidgetSettingsField[]>(
  () => schema.data.value?.fields ?? [],
)
const saved: ComputedRef<Values> = computed<Values>(() => props.widget.settings ?? {})

function savedValue(field: WidgetSettingsField): unknown {
  return field.key in saved.value
    ? saved.value[field.key]
    : field.default
}

const draft: Ref<Values> = ref({})
const result: Ref<SaveResult> = ref('none')

function takeSaved(): void {
  draft.value = Object.fromEntries(fields.value.map((field) => [field.key, savedValue(field)]))
}

watch([() => props.widget.id, fields], takeSaved, { immediate: true })
watch(
  () => props.widget.id,
  () => {
    result.value = 'none'
  },
)

const edited = computed<WidgetSettingsField[]>(() =>
  fields.value.filter((field) => !sameValue(draft.value[field.key], savedValue(field))),
)
const dirty = computed<boolean>(() => edited.value.length > 0)

function isChanged(field: WidgetSettingsField): boolean {
  return !sameValue(draft.value[field.key], field.default)
}

function set(key: string, value: unknown): void {
  draft.value = { ...draft.value, [key]: value }
  result.value = 'none'
}

interface Group {
  name: string
  fields: WidgetSettingsField[]
}
const groups: ComputedRef<Group[]> = computed<Group[]>(() => {
  const byName = new Map()

  for (const field of fields.value) {
    const lastKeyPart = field.group.key.split('.').pop() ?? ''
    const name = text(field.group, humanize(lastKeyPart))

    byName.set(name, [...(byName.get(name) ?? []), field])
  }

  return [...byName].map(([name, list]): Group => ({ name, fields: list }))
})

const rawKeys = computed<KeyValueItem[]>(() =>
  Object.entries(saved.value).map(([key, value]): KeyValueItem => ({
    label: key,
    value: JSON.stringify(value),
  })),
)

const state = computed<PanelState>(() => {
  if (schema.isPending.value)
    return 'loading'

  if (schema.isError.value)
    return 'error'

  return fields.value.length === 0 && rawKeys.value.length === 0
    ? 'empty'
    : 'ready'
})

const hint = computed<string>(() => {
  const base: string = 'dashboard.overlays.detail.settings'

  if (result.value === 'failed')
    return t(`${base}.failed`)

  if (dirty.value)
    return t(`${base}.unsaved`)

  return t(result.value === 'saved'
    ? `${base}.saved`
    : `${base}.allSaved`)
})

// Only the edited values travel over the saved map, so keys the schema does not list are kept.
function submit(): void {
  const changes: Values = Object.fromEntries(
    edited.value.map((field) => [field.key, draft.value[field.key]]),
  )

  save.mutate(
    { widgetId: props.widget.id, settings: { ...saved.value, ...changes } },
    {
      onSuccess: (): void => {
        takeSaved()
        result.value = 'saved'
      },
      onError: (): void => {
        result.value = 'failed'
      },
    },
  )
}
</script>

<template>
  <PanelCard
    data-slot="overlay-settings"
    :title="$t('dashboard.overlays.detail.settings.title')"
    :state="state"
    :empty-text="$t('dashboard.overlays.detail.settings.none')"
    :error-text="$t('dashboard.overlays.detail.settings.error')"
    @retry="schema.refetch()"
  >
    <div v-if="fields.length > 0" class="grid gap-4">
      <section v-for="group in groups" :key="group.name" class="grid gap-2">
        <h3 class="px-1 text-sm font-medium text-muted-foreground">{{ group.name }}</h3>
        <RowGroup>
          <OverlaySettingField
            v-for="field in group.fields"
            :key="field.key"
            :field="field"
            :value="draft[field.key]"
            :changed="isChanged(field)"
            @update="set(field.key, $event)"
            @reset="set(field.key, field.default)"
          />
        </RowGroup>
      </section>
    </div>
    <div v-else class="grid gap-2 p-2">
      <p class="text-sm text-muted-foreground">
        {{ $t('dashboard.overlays.detail.settings.raw') }}
      </p>
      <KeyValueList :items="rawKeys" />
    </div>

    <template v-if="state === 'ready' && fields.length > 0" #footer>
      <span
        class="min-w-0 flex-1 text-sm"
        :class="result === 'failed' ? 'text-destructive' : 'text-muted-foreground'"
        role="status"
        aria-live="polite"
        >{{ hint }}</span
      >
      <Button
        data-save
        variant="outline"
        size="sm"
        :disabled="!dirty || save.isPending.value"
        @click="submit"
      >
        <Save aria-hidden="true" />
        {{ $t('dashboard.overlays.detail.settings.save') }}
      </Button>
    </template>
  </PanelCard>
</template>
