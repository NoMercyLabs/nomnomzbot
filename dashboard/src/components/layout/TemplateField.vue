<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A text field for a template (spec row Textarea + Command): typing "{" suggests the helpers of its place.
// Arrow keys move, Enter or Tab inserts "{name}", Esc closes; focus never leaves the textarea.
// The last caret is remembered so a HelperPanel can insert there after focus moved away (insert()).
// A "{name}" no helper accepts gets a quiet note under the field; the text is never blocked.
import {
  computed,
  nextTick,
  ref,
  useId,
  useTemplateRef,
  type ComponentPublicInstance,
  type Ref,
} from 'vue'
import { useI18n } from 'vue-i18n'
import AlertTriangle from '~icons/nnz/alert-triangle'

import { useTemplateHelpers, type TemplateContext, type TemplateHelper } from '@/api/templates'
import { Command, CommandGroup, CommandItem, CommandList } from '@/components/ui/command'
import { Field, FieldLabel } from '@/components/ui/field'
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover'
import { Textarea } from '@/components/ui/textarea'

import { useHelperText } from './useHelperText'
import {
  braced,
  filterHelpers,
  openPlaceholder,
  unknownPlaceholders,
  type OpenPlaceholder,
} from './templatePlaceholders'

const props = withDefaults(
  defineProps<{
    label: string
    context: TemplateContext
    eventType?: string
    placeholder?: string
  }>(),
  { eventType: undefined, placeholder: undefined },
)

const text = defineModel<string>({ default: '' })
const { t } = useI18n()
const { describe } = useHelperText()
const id = useId()

const area = useTemplateRef<ComponentPublicInstance>('area')
const helpers = useTemplateHelpers(
  () => props.context,
  () => props.eventType,
)
const known = computed<TemplateHelper[]>(() => helpers.data.value ?? [])

const caret: Ref<{ start: number; end: number }> = ref({
  start: text.value.length,
  end: text.value.length,
})
const open: Ref<OpenPlaceholder | null> = ref(null)
const active = ref(0)

const matches = computed<TemplateHelper[]>(() =>
  open.value ? filterHelpers(known.value, open.value.query) : [],
)
const showing = computed<boolean>(() => matches.value.length > 0)
const unknown = computed<string[]>(() =>
  known.value.length > 0 ? unknownPlaceholders(text.value, known.value) : [],
)

function element(): HTMLTextAreaElement | null {
  return area.value?.$el instanceof HTMLTextAreaElement ? area.value.$el : null
}

function remember(): void {
  const el = element()

  if (el) caret.value = { start: el.selectionStart, end: el.selectionEnd }
}

// Suggestions follow typing and clicks, not arrow keys: Esc must stay closed until the person types again.
function suggest(): void {
  const el = element()

  if (!el) return
  remember()
  open.value =
    el.selectionStart === el.selectionEnd ? openPlaceholder(el.value, el.selectionStart) : null
  active.value = 0
}

async function replace(from: number, to: number, insertion: string): Promise<void> {
  const el = element()
  const current = text.value

  text.value = current.slice(0, from) + insertion + current.slice(to)
  const end = from + insertion.length

  caret.value = { start: end, end }
  open.value = null
  await nextTick()
  el?.focus()
  el?.setSelectionRange(end, end)
}

// Used by a HelperPanel: puts the text where the caret last was, over any selection.
function insert(insertion: string): Promise<void> {
  return replace(caret.value.start, caret.value.end, insertion)
}

function choose(helper: TemplateHelper | undefined): void {
  if (!helper || !open.value) return
  void replace(open.value.from, caret.value.start, braced(helper.key))
}

function onKeydown(event: KeyboardEvent): void {
  if (!showing.value) return
  const last = matches.value.length - 1

  if (event.key === 'ArrowDown') active.value = active.value >= last ? 0 : active.value + 1
  else if (event.key === 'ArrowUp') active.value = active.value <= 0 ? last : active.value - 1
  else if (event.key === 'Enter' || event.key === 'Tab') choose(matches.value[active.value])
  else if (event.key === 'Escape') open.value = null
  else return
  event.preventDefault()
}

defineExpose({ insert })
</script>

<template>
  <Field>
    <FieldLabel :for="id">{{ label }}</FieldLabel>
    <Popover :open="showing">
      <PopoverAnchor as-child>
        <Textarea
          :id="id"
          ref="area"
          v-model="text"
          :placeholder="placeholder"
          aria-autocomplete="list"
          aria-haspopup="listbox"
          :aria-expanded="showing"
          @input="suggest"
          @click="suggest"
          @keyup="remember"
          @select="remember"
          @keydown="onKeydown"
        />
      </PopoverAnchor>
      <PopoverContent
        align="start"
        class="w-(--reka-popover-anchor-width) gap-0 p-0"
        @open-auto-focus.prevent
        @close-auto-focus.prevent
        @interact-outside.prevent
        @focus-outside.prevent
      >
        <Command :highlight-on-hover="false">
          <CommandList>
            <CommandGroup>
              <CommandItem
                v-for="(helper, index) in matches"
                :key="helper.key"
                :value="helper.key"
                :data-helper="helper.key"
                :class="index === active ? 'bg-muted text-foreground' : ''"
                @mousedown.prevent
                @select="choose(helper)"
              >
                <span class="font-mono font-medium">{{ braced(helper.key) }}</span>
                <span class="min-w-0 flex-1 truncate text-muted-foreground">{{
                  describe(helper)
                }}</span>
              </CommandItem>
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
    <p
      v-if="unknown.length > 0"
      data-template-warning
      role="status"
      class="flex items-start gap-1.5 text-xs text-muted-foreground"
    >
      <AlertTriangle class="mt-px size-3.5 shrink-0" />{{
        t('template.field.unknown', { names: unknown.join(' ') })
      }}
    </p>
  </Field>
</template>
