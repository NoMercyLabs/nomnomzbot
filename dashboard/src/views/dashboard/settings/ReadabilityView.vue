<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How text looks for this person on every page. There is no server endpoint for it: the options live in
// this browser (useReadability) and apply at once, so the footer says "saved on this device".
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SegmentedControl from '@/components/layout/SegmentedControl.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import { Button } from '@/components/ui/button'
import { Slider } from '@/components/ui/slider'
import {
  readabilitySizeMax,
  readabilitySizeMin,
  useReadability,
  type Readability,
  type ReadabilityPreset,
  type UseReadability,
} from '@/composables/useReadability'

import ReadabilityRow from './components/ReadabilityRow.vue'

const { t } = useI18n()
const { settings, activePreset, applyPreset }: UseReadability = useReadability()

interface Choice<T extends string> {
  value: T
  label: string
}

const presets: ComputedRef<Choice<ReadabilityPreset>[]> = computed<Choice<ReadabilityPreset>[]>(
  () => [
    { value: 'default', label: t('dashboard.settings.readability.presets.default') },
    { value: 'dyslexia', label: t('dashboard.settings.readability.presets.dyslexia') },
    { value: 'contrast', label: t('dashboard.settings.readability.presets.contrast') },
  ],
)
const fonts: ComputedRef<Choice<Readability['font']>[]> = computed<Choice<Readability['font']>[]>(
  () => [
    { value: 'system', label: t('dashboard.settings.readability.font.system') },
    { value: 'hyperlegible', label: t('dashboard.settings.readability.font.hyperlegible') },
    { value: 'lexend', label: t('dashboard.settings.readability.font.lexend') },
  ],
)
const lineSpacings: ComputedRef<Choice<Readability['lh']>[]> = computed<
  Choice<Readability['lh']>[]
>(() => [
  { value: 'normal', label: t('dashboard.settings.readability.lineSpacing.normal') },
  { value: 'wide', label: t('dashboard.settings.readability.lineSpacing.wide') },
  { value: 'wider', label: t('dashboard.settings.readability.lineSpacing.wider') },
])
const spacings: ComputedRef<Choice<Readability['spacing']>[]> = computed<
  Choice<Readability['spacing']>[]
>(() => [
  { value: 'normal', label: t('dashboard.settings.readability.spacing.normal') },
  { value: 'wide', label: t('dashboard.settings.readability.spacing.wide') },
])
const motions: ComputedRef<Choice<Readability['motion']>[]> = computed<
  Choice<Readability['motion']>[]
>(() => [
  { value: 'system', label: t('dashboard.settings.readability.motion.system') },
  { value: 'reduce', label: t('dashboard.settings.readability.motion.reduce') },
])
const densities: ComputedRef<Choice<Readability['density']>[]> = computed<
  Choice<Readability['density']>[]
>(() => [
  { value: 'compact', label: t('dashboard.settings.readability.density.compact') },
  { value: 'comfortable', label: t('dashboard.settings.readability.density.comfortable') },
  { value: 'touch', label: t('dashboard.settings.readability.density.touch') },
])

function setSize(next: number[] | undefined): void {
  const size: number | undefined = next?.[0]
  if (size !== undefined) settings.value = { ...settings.value, size }
}
</script>

<template>
  <PanelCard
    data-view="readability"
    pad
    :title="$t('dashboard.settings.readability.title')"
    :description="$t('dashboard.settings.readability.description')"
  >
    <div class="grid gap-6">
      <section class="grid gap-2" aria-labelledby="rd-presets">
        <div class="px-3">
          <h3 id="rd-presets" class="text-sm font-semibold">
            {{ $t('dashboard.settings.readability.presets.title') }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ $t('dashboard.settings.readability.presets.description') }}
          </p>
        </div>
        <div
          class="flex flex-wrap gap-2 px-3"
          role="group"
          :aria-label="$t('dashboard.settings.readability.presets.title')"
        >
          <Button
            v-for="preset in presets"
            :key="preset.value"
            variant="outline"
            size="sm"
            :aria-pressed="activePreset === preset.value"
            :class="activePreset === preset.value ? 'bg-muted' : ''"
            @click="applyPreset(preset.value)"
          >
            {{ preset.label }}
          </Button>
        </div>
      </section>

      <section class="grid gap-2" aria-labelledby="rd-text">
        <div class="px-3">
          <h3 id="rd-text" class="text-sm font-semibold">
            {{ $t('dashboard.settings.readability.text.title') }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ $t('dashboard.settings.readability.text.description') }}
          </p>
        </div>
        <div
          data-slot="readability-sample"
          class="mx-3 rounded-[12px] border border-border bg-background p-4"
          aria-live="polite"
        >
          <!-- The words file holds the spaces: template whitespace across line breaks is dropped. -->
          <i18n-t keypath="dashboard.settings.readability.sample.line" tag="p" scope="global">
            <template #name>
              <b>{{ $t('dashboard.settings.readability.sample.name') }}</b>
            </template>
            <template #time>
              <span class="text-muted-foreground">{{
                $t('dashboard.settings.readability.sample.time')
              }}</span>
            </template>
          </i18n-t>
          <p class="text-muted-foreground">
            {{ $t('dashboard.settings.readability.sample.pangram') }}
          </p>
        </div>
        <RowGroup>
          <ReadabilityRow
            :title="$t('dashboard.settings.readability.font.title')"
            :description="$t('dashboard.settings.readability.font.description')"
          >
            <SegmentedControl
              v-model="settings.font"
              :label="$t('dashboard.settings.readability.font.title')"
              :options="fonts"
            />
          </ReadabilityRow>
          <ReadabilityRow
            :title="$t('dashboard.settings.readability.size.title')"
            :description="$t('dashboard.settings.readability.size.description')"
          >
            <Slider
              class="w-56 **:data-[slot=slider-track]:bg-border"
              :model-value="[settings.size]"
              :min="readabilitySizeMin"
              :max="readabilitySizeMax"
              :step="1"
              :aria-label="$t('dashboard.settings.readability.size.title')"
              @update:model-value="setSize"
            />
            <span class="min-w-11 text-right text-sm tabular-nums">{{
              $t('dashboard.settings.readability.size.value', { size: settings.size })
            }}</span>
          </ReadabilityRow>
          <ReadabilityRow :title="$t('dashboard.settings.readability.lineSpacing.title')">
            <SegmentedControl
              v-model="settings.lh"
              :label="$t('dashboard.settings.readability.lineSpacing.title')"
              :options="lineSpacings"
            />
          </ReadabilityRow>
          <ReadabilityRow
            :title="$t('dashboard.settings.readability.spacing.title')"
            :description="$t('dashboard.settings.readability.spacing.description')"
          >
            <SegmentedControl
              v-model="settings.spacing"
              :label="$t('dashboard.settings.readability.spacing.title')"
              :options="spacings"
            />
          </ReadabilityRow>
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="rd-motion">
        <div class="px-3">
          <h3 id="rd-motion" class="text-sm font-semibold">
            {{ $t('dashboard.settings.readability.contrastMotion.title') }}
          </h3>
        </div>
        <RowGroup>
          <SettingRow
            v-model="settings.contrast"
            :title="$t('dashboard.settings.readability.contrast.title')"
            :description="$t('dashboard.settings.readability.contrast.description')"
          />
          <ReadabilityRow
            :title="$t('dashboard.settings.readability.motion.title')"
            :description="$t('dashboard.settings.readability.motion.description')"
          >
            <SegmentedControl
              v-model="settings.motion"
              :label="$t('dashboard.settings.readability.motion.title')"
              :options="motions"
            />
          </ReadabilityRow>
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="rd-density">
        <div class="px-3">
          <h3 id="rd-density" class="text-sm font-semibold">
            {{ $t('dashboard.settings.readability.density.title') }}
          </h3>
        </div>
        <RowGroup>
          <ReadabilityRow
            :title="$t('dashboard.settings.readability.density.control')"
            :description="$t('dashboard.settings.readability.density.description')"
          >
            <SegmentedControl
              v-model="settings.density"
              :label="$t('dashboard.settings.readability.density.title')"
              :options="densities"
            />
          </ReadabilityRow>
        </RowGroup>
      </section>
    </div>
    <template #footer>
      <span class="text-sm text-muted-foreground">{{
        $t('dashboard.settings.readability.footer')
      }}</span>
    </template>
  </PanelCard>
</template>
