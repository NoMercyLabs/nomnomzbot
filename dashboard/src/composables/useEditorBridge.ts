// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useEventListener } from '@vueuse/core'

// The host side of the served code editor page (/editor/index.html, owned by the server's editor.js).
// A port of the Compose host's EditorBridgeProtocol and EditorBridgeSession: the page sends requests,
// the host answers each one with exactly one reply, and never stays silent when a capability is missing.

export type EditorFiles = Record<string, string>
export type EditorVariables = Record<string, string>
type JsonObject = Record<string, unknown>

export type BuildError = {
  code?: string
  message: string
  file?: string
  line?: number
  column?: number
}
export type CompileFeedback = { ok: boolean; message: string; errors?: BuildError[] }
export type VersionSummary = {
  id: string
  version: number
  validationStatus: string
  isCurrent: boolean
}
export type VersionsPage = { versions: VersionSummary[]; hasMore: boolean }
export type EditorOutcome<T> = { ok: true; value: T } | { ok: false; message: string }

export type TestTrigger = { id: string; label: string; variables: EditorVariables }
export type TestRunRequest = {
  files: EditorFiles
  variables: EditorVariables
  args: string[]
  trigger: string | null
  role: string | null
}
export type TestRunResult = {
  success: boolean
  durationMs: number
  hostCallCount: number
  error: string | null
  chatOutput: string[]
  variablesSet: EditorVariables | null
  timeline: { seq: number; kind: string; text: string }[]
  console: string[] | null
  effects: { name: string; argsPreview: string }[]
  errors?: BuildError[]
}

export type PreviewActionRequest = {
  requestId: string
  actionType: string
  params: JsonObject | null
  variables: EditorVariables
}
export type PreviewActionResult = {
  success: boolean
  output: string | null
  error: string | null
  errorCode: string | null
  variables: EditorVariables
}

export type EditorOpenPayload = {
  title: string
  files: EditorFiles
  entry: string
  language: string
  sdkTypes: string
  sdkTypesUnavailable: boolean
  labels: Record<string, unknown>
  fireSamples?: JsonObject
  fireSamplesError?: string
  eventSubscriptions?: string[]
  widget?: { id: string; name: string; settings: JsonObject }
  history?: VersionsPage
  testRunEnabled?: boolean
  testTriggers?: TestTrigger[]
}

export type EditorHistoryRunner = {
  loadMore: () => Promise<EditorOutcome<VersionsPage>>
  rollback: (versionId: string) => Promise<EditorOutcome<VersionsPage>>
  remove: (versionId: string) => Promise<EditorOutcome<VersionsPage>>
}

export type EditorBridgeOptions = {
  // The origin both sides require: messages are posted to it and anything from another origin is dropped.
  origin: string
  target: () => Pick<Window, 'postMessage'> | null
  payload: () => EditorOpenPayload
  onSave: (files: EditorFiles) => Promise<CompileFeedback>
  onClose: () => void
  history?: EditorHistoryRunner
  testRun?: (request: TestRunRequest) => Promise<EditorOutcome<TestRunResult>>
  previewAction?: (request: PreviewActionRequest) => Promise<PreviewActionResult>
}

export type EditorBridge = {
  handle: (event: MessageEvent) => Promise<void>
  requestClose: () => void
}

const PREFIX: string = 'nnz:editor:'

type PageMessage =
  | { type: 'ready' } |
  { type: 'save'; files: EditorFiles } |
  { type: 'close' } |
  { type: 'historyLoadMore' } |
  { type: 'historyRollback'; versionId: string } |
  { type: 'historyDelete'; versionId: string } |
  ({ type: 'testRun' } & TestRunRequest) |
  ({ type: 'previewAction' } & PreviewActionRequest)

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function stringMap(value: unknown): Record<string, string> {
  if (!isObject(value))
    return {}

  const out: Record<string, string> = {}

  for (const [key, entry] of Object.entries(value)) {
    if (typeof entry === 'string') {
      out[key] = entry
    }
  }

  return out
}

function text(value: unknown): string {
  return typeof value === 'string'
    ? value
    : ''
}

function optionalText(value: unknown): string | null {
  return typeof value === 'string'
    ? value
    : null
}

// One page message, or null for anything else: malformed data, a host-to-page type echoed back, other traffic.
export function decodePageMessage(data: unknown): PageMessage | null {
  if (!isObject(data) || typeof data.type !== 'string' || !data.type.startsWith(PREFIX))
    return null

  switch (data.type.slice(PREFIX.length)) {
    case 'ready':
      return { type: 'ready' }
    case 'save':
      return { type: 'save', files: stringMap(data.files) }
    case 'close':
      return { type: 'close' }
    case 'historyLoadMore':
      return { type: 'historyLoadMore' }
    case 'historyRollback':
      return { type: 'historyRollback', versionId: text(data.versionId) }
    case 'historyDelete':
      return { type: 'historyDelete', versionId: text(data.versionId) }
    case 'testRun':
      return {
        type: 'testRun',
        files: stringMap(data.files),
        variables: stringMap(data.variables),
        args: Array.isArray(data.args)
          ? data.args.filter((arg): arg is string => typeof arg === 'string')
          : [],
        trigger: optionalText(data.trigger),
        role: optionalText(data.role),
      }
    case 'previewAction':
      return {
        type: 'previewAction',
        requestId: text(data.requestId),
        actionType: text(data.actionType),
        params: isObject(data.params)
          ? data.params
          : null,
        variables: stringMap(data.variables),
      }
    default:
      return null
  }
}

function reason(error: unknown): string {
  return error instanceof Error && error.message
    ? error.message
    : 'The action failed.'
}

export function createEditorBridge(options: EditorBridgeOptions): EditorBridge {
  function post(type: string, body: JsonObject = {}): void {
    options.target()?.postMessage({ type: PREFIX + type, ...body }, options.origin)
  }

  function open(): void {
    const payload = options.payload()

    post('open', {
      payload: {
        fireSamples: {},
        eventSubscriptions: [],
        testRunEnabled: options.testRun !== undefined,
        testTriggers: [],
        ...payload,
      },
    })
  }

  async function save(files: EditorFiles): Promise<void> {
    try {
      const feedback = await options.onSave(files)
      const errors: BuildError[] = feedback.errors ?? []

      post('compiled', {
        ok: feedback.ok,
        message: feedback.message,
        ...(errors.length > 0
          ? { errors }
          : {}),
      })
    } catch (error) {
      post('compiled', { ok: false, message: reason(error) })
    }
  }

  async function history(
    run: (runner: EditorHistoryRunner) => Promise<EditorOutcome<VersionsPage>>,
  ): Promise<void> {
    if (!options.history) {
      post('historyError', { message: 'History is not available for this project.' })

      return
    }

    try {
      const outcome = await run(options.history)

      if (outcome.ok) {
        post('historyPage', { payload: outcome.value })
      }
      else {
        post('historyError', { message: outcome.message })
      }
    } catch (error) {
      post('historyError', { message: reason(error) })
    }
  }

  async function testRun(request: TestRunRequest): Promise<void> {
    if (!options.testRun) {
      post('testRunResult', { ok: false, message: 'Test run is not available for this project.' })

      return
    }

    try {
      const outcome = await options.testRun(request)

      if (outcome.ok) {
        post('testRunResult', { ok: true, ...outcome.value })
      }
      else {
        post('testRunResult', { ok: false, message: outcome.message })
      }
    } catch (error) {
      post('testRunResult', { ok: false, message: reason(error) })
    }
  }

  // Every request gets exactly one reply: the page times out on silence.
  async function previewAction(request: PreviewActionRequest): Promise<void> {
    let result: PreviewActionResult = {
      success: false,
      output: null,
      error: null,
      errorCode: 'PREVIEW_ACTION_UNAVAILABLE',
      variables: {},
    }

    if (options.previewAction) {
      try {
        result = await options.previewAction(request)
      } catch (error) {
        result = {
          success: false,
          output: null,
          error: reason(error),
          errorCode: 'PREVIEW_ACTION_FAILED',
          variables: {},
        }
      }
    }

    post('previewActionResult', { requestId: request.requestId, ...result })
  }

  async function handle(event: MessageEvent): Promise<void> {
    // Same origin is not enough: any frame of this origin could post, so only the editor's own window counts.
    if (event.origin !== options.origin || event.source !== options.target()) return

    const message = decodePageMessage(event.data)

    if (!message) return

    switch (message.type) {
      case 'ready':
        return open()
      case 'save':
        return save(message.files)
      case 'close':
        return options.onClose()
      case 'historyLoadMore':
        return history((runner) => runner.loadMore())
      case 'historyRollback':
        return history((runner) => runner.rollback(message.versionId))
      case 'historyDelete':
        return history((runner) => runner.remove(message.versionId))
      case 'testRun':
        return testRun(message)
      case 'previewAction':
        return previewAction(message)
    }
  }

  return { handle, requestClose: () => post('requestClose') }
}

// Listens on the window for as long as the calling component lives.
export function useEditorBridge(options: EditorBridgeOptions): EditorBridge {
  const bridge = createEditorBridge(options)

  useEventListener(window, 'message', (event: MessageEvent) => {
    void bridge.handle(event)
  })

  return bridge
}
