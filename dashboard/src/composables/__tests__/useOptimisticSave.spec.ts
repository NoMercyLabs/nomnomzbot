// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin, type QueryKey } from '@tanstack/vue-query'
import { createPinia, setActivePinia } from 'pinia'
import { createApp, type App } from 'vue'
import { describe, expect, it } from 'vitest'

import { useSaveErrorsStore } from '@/stores/saveErrors'

import { useOptimisticSave } from '../useOptimisticSave'

type Settings = { greeting: string }
type Deferred = {
  promise: Promise<Settings>
  resolve: (value: Settings) => void
  reject: (error: Error) => void
}

const key: string[] = ['channels', 'c1', 'settings']
const before = { greeting: 'before' } as const satisfies Settings

function deferred(): Deferred {
  let resolve: (value: Settings) => void = () => {}
  let reject: (error: Error) => void = () => {}
  const promise: Promise<Settings> = new Promise((done, fail) => {
    resolve = done
    reject = fail
  })

  return { promise, resolve, reject }
}

function setup(
  send: (greeting: string) => Promise<Settings>,
  queryKey: QueryKey | ((greeting: string) => QueryKey) = key,
) {
  const queries = new QueryClient()

  queries.setQueryData(key, before)
  const pinia = createPinia()

  setActivePinia(pinia)
  let save!: ReturnType<typeof useOptimisticSave<Settings, string>>
  const app: App = createApp({
    setup() {
      save = useOptimisticSave<Settings, string>({
        queryKey,
        mutationFn: send,
        apply: (_current, greeting) => ({ greeting }),
      })

      return () => null
    },
  })

  app.use(pinia)
  app.use(VueQueryPlugin, { queryClient: queries })
  app.mount(document.createElement('div'))

  return { queries, save, inbox: useSaveErrorsStore() }
}

const cached = (queries: QueryClient): Settings | undefined => queries.getQueryData<Settings>(key)
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0))

describe('useOptimisticSave', () => {
  it('changes the cached value before the server answers', async () => {
    const pending = deferred()
    const { queries, save } = setup(() => pending.promise)

    void save.mutateAsync('after')
    await settle()

    expect(cached(queries)).toEqual({ greeting: 'after' })
    pending.resolve({ greeting: 'after' })
  })

  it('restores the exact earlier value and reports to the error inbox when the server fails', async () => {
    const pending = deferred()
    const { queries, save, inbox } = setup(() => pending.promise)

    const result: Promise<unknown> = save.mutateAsync('after').catch((error: Error) => error)

    await settle()
    pending.reject(new Error('503'))
    await result

    expect(cached(queries)).toStrictEqual({ greeting: 'before' })
    expect(inbox.items).toHaveLength(1)
    expect(inbox.items[0]).toMatchObject({
      titleKey: 'frame.inbox.saveFailedTitle',
      messageKey: 'frame.inbox.saveFailedMessage',
    })
  })

  it('keeps the value the server returned on success', async () => {
    const { queries, save, inbox } = setup(async (greeting) => ({
      greeting: `${greeting} (server)`,
    }))

    await save.mutateAsync('after')

    expect(cached(queries)).toEqual({ greeting: 'after (server)' })
    expect(inbox.items).toHaveLength(0)
  })

  it('ends on the last of two fast saves, whatever order the server answers in', async () => {
    const first = deferred()
    const second = deferred()
    const answers: Deferred[] = [first, second]
    const { queries, save } = setup(() => (answers.shift() as Deferred).promise)

    const one = save.mutateAsync('one')

    await settle()
    const two = save.mutateAsync('two')

    await settle()
    expect(cached(queries)).toEqual({ greeting: 'two' })

    second.resolve({ greeting: 'two (server)' })
    await two
    first.resolve({ greeting: 'one (server)' })
    await one

    expect(cached(queries)).toEqual({ greeting: 'two (server)' })
  })

  it('does not roll the screen back past a later save when an earlier save fails', async () => {
    const first = deferred()
    const second = deferred()
    const answers: Deferred[] = [first, second]
    const { queries, save } = setup(() => (answers.shift() as Deferred).promise)

    const one: Promise<unknown> = save.mutateAsync('one').catch((error: Error) => error)

    await settle()
    const two = save.mutateAsync('two')

    await settle()

    first.reject(new Error('500'))
    await one
    expect(cached(queries)).toEqual({ greeting: 'two' })

    second.resolve({ greeting: 'two (server)' })
    await two
    expect(cached(queries)).toEqual({ greeting: 'two (server)' })
  })

  it('saves each change into the cache entry its own key names, and rolls back only that entry', async () => {
    const other: string[] = ['channels', 'c1', 'other']
    const first = deferred()
    const second = deferred()
    const answers: Deferred[] = [first, second]
    const { queries, save } = setup(
      () => (answers.shift() as Deferred).promise,
      (greeting: string) =>
        greeting.startsWith('other')
          ? other
          : key,
    )

    queries.setQueryData(other, { greeting: 'other before' })

    const one: Promise<unknown> = save.mutateAsync('mine').catch((error: Error) => error)

    await settle()
    const two = save.mutateAsync('other after')

    await settle()
    expect(cached(queries)).toEqual({ greeting: 'mine' })
    expect(queries.getQueryData(other)).toEqual({ greeting: 'other after' })

    first.reject(new Error('500'))
    await one
    second.resolve({ greeting: 'other after (server)' })
    await two

    expect(cached(queries)).toStrictEqual({ greeting: 'before' })
    expect(queries.getQueryData(other)).toEqual({ greeting: 'other after (server)' })
  })
})
