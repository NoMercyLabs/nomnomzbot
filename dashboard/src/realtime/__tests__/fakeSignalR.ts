// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A stand-in for @microsoft/signalr: records every call and lets a test push a server event.
export class FakeHubConnection {
  static instances: FakeHubConnection[] = []

  static at(index: number): FakeHubConnection {
    const found: FakeHubConnection | undefined = FakeHubConnection.instances[index]
    if (!found) throw new Error(`no connection ${index} was opened`)
    return found
  }

  readonly invocations: [string, ...unknown[]][] = []
  readonly handlers: Map<string, ((payload: unknown) => void)[]> = new Map()
  private readonly callbacks: Record<'reconnecting' | 'reconnected' | 'close', (() => void)[]> = {
    reconnecting: [],
    reconnected: [],
    close: [],
  }
  // The same calls a real connection makes when the transport drops, recovers or closes.
  readonly lifecycle: Record<'reconnecting' | 'reconnected' | 'close', () => void> = {
    reconnecting: () => this.callbacks.reconnecting.forEach((callback) => callback()),
    reconnected: () => this.callbacks.reconnected.forEach((callback) => callback()),
    close: () => this.callbacks.close.forEach((callback) => callback()),
  }
  stopped = false
  url = ''

  constructor() {
    FakeHubConnection.instances.push(this)
  }

  on(event: string, handler: (payload: unknown) => void): void {
    this.handlers.set(event, [...(this.handlers.get(event) ?? []), handler])
  }
  onreconnecting(callback: () => void): void {
    this.callbacks.reconnecting.push(callback)
  }
  onreconnected(callback: () => void): void {
    this.callbacks.reconnected.push(callback)
  }
  onclose(callback: () => void): void {
    this.callbacks.close.push(callback)
  }
  start(): Promise<void> {
    return Promise.resolve()
  }
  stop(): Promise<void> {
    this.stopped = true
    this.lifecycle.close()
    return Promise.resolve()
  }
  invoke(method: string, ...args: unknown[]): Promise<{ success: boolean }> {
    this.invocations.push([method, ...args])
    return Promise.resolve({ success: true })
  }
  receive(event: string, payload: unknown): void {
    for (const handler of this.handlers.get(event) ?? []) handler(payload)
  }
}

export class FakeHubConnectionBuilder {
  private url = ''
  withUrl(url: string): this {
    this.url = url
    return this
  }
  withAutomaticReconnect(): this {
    return this
  }
  build(): FakeHubConnection {
    const connection: FakeHubConnection = new FakeHubConnection()
    connection.url = this.url
    return connection
  }
}
