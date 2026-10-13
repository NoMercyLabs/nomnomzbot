<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts" generic="T">
// The V11 long list (chat, people, history, logs): only the rows in view are in the DOM, so 10,000
// rows scroll as fast as 10. Rows are keyed by the item (not by position) and measured as they
// render, so a row may wrap. Give it the height the parent allows (flex-1 min-h-0 or a fixed height).
// stick: a chat-style list. At the bottom it stays at the bottom as rows arrive. Scrolled up, the
// reader keeps their place, new rows are counted, and one button returns to the bottom.
import { useVirtualizer, type PartialKeys, type VirtualizerOptions } from '@tanstack/vue-virtual'
import { computed, nextTick, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'

import { Button } from '@/components/ui/button'

const props = withDefaults(
  defineProps<{
    items: readonly T[]
    itemKey: (item: T, index: number) => string | number
    estimateSize: number
    label: string
    stick?: boolean
  }>(),
  { stick: false },
)

defineSlots<{ default(props: { item: T; index: number }): unknown }>()

const AT_BOTTOM_SLACK = 8
const OVERSCAN = 8

const scrollArea: Ref<HTMLElement | null> = ref(null)
const atBottom = ref(true)
const newBelow = ref(0)

type Options = PartialKeys<
  VirtualizerOptions<HTMLElement, Element>,
  'observeElementRect' | 'observeElementOffset' | 'scrollToFn'
>

const options: ComputedRef<Options> = computed(() => ({
  count: props.items.length,
  getScrollElement: () => scrollArea.value,
  estimateSize: () => props.estimateSize,
  getItemKey: (index: number) => props.itemKey(props.items[index]!, index),
  overscan: OVERSCAN,
}))
const virtualizer = useVirtualizer(options)

function measure(element: unknown): void {
  if (element instanceof Element) {
    virtualizer.value.measureElement(element)
  }
}

function viewportHeight(element: HTMLElement): number {
  return virtualizer.value.scrollRect?.height ?? element.clientHeight
}

function maxOffset(element: HTMLElement): number {
  return Math.max(0, virtualizer.value.getTotalSize() - viewportHeight(element))
}

function onScroll(): void {
  const element = scrollArea.value

  if (!element) return

  atBottom.value = maxOffset(element) - element.scrollTop <= AT_BOTTOM_SLACK

  if (atBottom.value) {
    newBelow.value = 0
  }
}

function scrollToBottom(): void {
  if (props.items.length === 0) return

  virtualizer.value.scrollToIndex(props.items.length - 1, { align: 'end' })
}

function jumpToBottom(): void {
  newBelow.value = 0
  scrollToBottom()
}

function scrollBy(delta: number): void {
  const element = scrollArea.value

  if (!element) return

  const wanted = Math.max(0, element.scrollTop + delta)
  const offset = Math.min(maxOffset(element), wanted)

  virtualizer.value.scrollToOffset(offset)
}

function onKey(event: KeyboardEvent): void {
  const element = scrollArea.value

  if (!element || event.target !== element) return

  const page = viewportHeight(element)
  const moves: Record<string, () => void> = {
    ArrowDown: () => scrollBy(props.estimateSize),
    ArrowUp: () => scrollBy(-props.estimateSize),
    PageDown: () => scrollBy(page),
    PageUp: () => scrollBy(-page),
    Home: () => scrollBy(-element.scrollTop),
    End: () => scrollBy(maxOffset(element) - element.scrollTop),
  }
  const move = moves[event.key]

  if (!move) return

  event.preventDefault()
  move()
}

watch(
  () => props.items.length,
  async (count: number, before: number) => {
    if (!props.stick) return

    if (count <= before) {
      newBelow.value = 0

      return
    }

    if (atBottom.value) {
      await nextTick()
      scrollToBottom()
    } else {
      newBelow.value += count - before
    }
  },
)

onMounted(async () => {
  if (!props.stick) return

  await nextTick()
  scrollToBottom()
})
</script>

<template>
  <div class="relative flex min-h-0 flex-1 flex-col">
    <div
      ref="scrollArea"
      data-virtual-scroll
      tabindex="0"
      role="region"
      :aria-label="label"
      class="min-h-0 flex-1 overflow-y-auto rounded-[8px] focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
      @scroll.passive="onScroll"
      @keydown="onKey"
    >
      <div
        role="list"
        class="relative w-full"
        :style="{ height: `${virtualizer.getTotalSize()}px` }"
      >
        <div
          v-for="row in virtualizer.getVirtualItems()"
          :key="String(row.key)"
          :ref="measure"
          role="listitem"
          :data-index="row.index"
          :data-key="String(row.key)"
          :aria-posinset="row.index + 1"
          :aria-setsize="items.length"
          class="absolute top-0 left-0 w-full"
          :style="{ transform: `translateY(${row.start}px)` }"
        >
          <slot :item="items[row.index]!" :index="row.index" />
        </div>
      </div>
    </div>
    <Button
      v-if="newBelow > 0"
      data-new-below
      variant="outline"
      size="sm"
      class="absolute bottom-3 left-1/2 -translate-x-1/2 shadow-md"
      @click="jumpToBottom"
    >
      {{ $t('common.list.newBelow', newBelow) }}
    </Button>
  </div>
</template>
