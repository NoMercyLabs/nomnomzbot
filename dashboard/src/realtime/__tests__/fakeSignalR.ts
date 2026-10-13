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
  static instances: FakeHubConnection[] = [];
  // How many start() calls fail before one succeeds, across all connections.
  static startFailures = 0;
  static startCalls = 0;

  static at(index: number): FakeHubConnection {
    const found = FakeHubConnection.instances[index];

    if (!found)
      throw new Error(`no connection ${index} was opened`);

    return found;
  }

  readonly invocations: [string, ...unknown[]][] = [];
  readonly handlers: Map<string, ((payload: unknown) => void)[]> = new Map();
  private readonly callbacks: Record<'reconnecting' | 'reconnected' | 'close', (() => void)[]> = {
    reconnecting: [],
    reconnected: [],
    close: [],
  };

  // The same calls a real connection makes when the transport drops, recovers or closes.
  readonly lifecycle: Record<'reconnecting' | 'reconnected' | 'close', () => void> = {
    reconnecting: () => this.callbacks.reconnecting.forEach((callback) => callback()),
    reconnected: () => this.callbacks.reconnected.forEach((callback) => callback()),
    close: () => this.callbacks.close.forEach((callback) => callback()),
  };

  stopped = false;
  url = '';

  constructor() {
    FakeHubConnection.instances.push(this);
  }

  on(event: string, handler: (payload: unknown) => void): void {
    this.handlers.set(event, [...(this.handlers.get(event) ?? []), handler]);
  }

  onreconnecting(callback: () => void): void {
    this.callbacks.reconnecting.push(callback);
  }

  onreconnected(callback: () => void): void {
    this.callbacks.reconnected.push(callback);
  }

  onclose(callback: () => void): void {
    this.callbacks.close.push(callback);
  }

  start(): Promise<void> {
    FakeHubConnection.startCalls += 1;

    if (FakeHubConnection.startFailures > 0) {
      FakeHubConnection.startFailures -= 1;

      return Promise.reject(new Error('the server is not reachable'));
    }

    return Promise.resolve();
  }

  stop(): Promise<void> {
    this.stopped = true;
    this.lifecycle.close();

    return Promise.resolve();
  }

  // What the server answers to a call; a test replaces it to make a join fail.
  answer: (method: string, args: unknown[]) => { success: boolean; error?: string } = () => ({
    success: true,
  });

  invoke(method: string, ...args: unknown[]): Promise<{ success: boolean; error?: string }> {
    this.invocations.push([method, ...args]);

    return Promise.resolve(this.answer(method, args));
  }

  receive(event: string, payload: unknown): void {
    for (const handler of this.handlers.get(event) ?? []) {
      handler(payload);
    }
  }
}

export type FakeRetryPolicy = {
  nextRetryDelayInMilliseconds: (context: {
    previousRetryCount: number;
    elapsedMilliseconds: number;
    retryReason: Error;
  }) => number | null;
};

export class FakeHubConnectionBuilder {
  static policy: FakeRetryPolicy | boolean | null = null;
  static logLevel: number | null = null;

  private url = '';
  configureLogging(level: number): this {
    FakeHubConnectionBuilder.logLevel = level;

    return this;
  }

  withUrl(url: string): this {
    this.url = url;

    return this;
  }

  withAutomaticReconnect(policy?: FakeRetryPolicy): this {
    FakeHubConnectionBuilder.policy = policy ?? true;

    return this;
  }

  build(): FakeHubConnection {
    const connection = new FakeHubConnection();

    connection.url = this.url;

    return connection;
  }
}

type SignalR = typeof import('@microsoft/signalr');

// The vi.mock factory every hub spec shares: the real module (enums such as LogLevel) with the fakes swapped in.
export async function fakeSignalR(importOriginal: () => Promise<SignalR>) {
  return {
    ...await importOriginal(),
    HubConnectionBuilder: FakeHubConnectionBuilder,
    HubConnection: FakeHubConnection,
  };
}
