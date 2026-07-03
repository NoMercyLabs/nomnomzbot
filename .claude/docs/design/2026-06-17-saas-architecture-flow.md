# SaaS & Self-Host Architecture — Stack + Flow

Maps the runtime. §1 is the ingress flow (two delivery paths → converge at the app instance → shared path);
§2 is the full SaaS entity topology; §3 shows what swaps for self-host; §4 is the flow zoomed in. Derived from
`spec/scaling-qos.md`, `spec/event-store.md`, `spec/twitch-eventsub.md`, `2026-06-16-deployment-profile.md`.

## 1. Ingress → converge at the app instance → shared path

The two deployments differ **only in how the event reaches an app instance** — and only the webhook path is
ACKed. They **converge inside the app instance**: whichever transport adapter is active builds the same
`EventSubNotification` and calls the same `INotificationDispatcher`, and everything from the dispatcher down is
identical code. There is no separate "merge box" — the convergence **is** the app instance.

```mermaid
flowchart TB
  subgraph TW["Twitch (external)"]
    direction LR
    TWH["webhook via conduit<br/>(SaaS)"]
    TWS["websocket<br/>(self-host)"]
  end

  NG["nginx<br/>(SaaS only)"]

  subgraph APP["App instance — the paths converge here"]
    direction TB
    XPORT["Transport adapter (one, per deployment)<br/>SaaS: webhook controller (verify HMAC)<br/>self-host: WS receive loop"]
    DISP["INotificationDispatcher<br/>(shared — identical from here down)"]
    DEDUP["Dedupe — UUIDv5 of message-id"]
    STORE["Store in durable log"]
    PUB["Dispatch 'event happened' → bus"]
    ROUTE["Route by type → handlers → outcomes<br/>chat reply · projections · webhook fan-out"]
    XPORT --> DISP --> DEDUP --> STORE --> PUB --> ROUTE
  end

  TWH --> NG --> XPORT
  TWS --> XPORT
  STORE -. "HTTP 200 ack — webhook only" .-> TWH
```

- **Only the delivery differs:** SaaS = webhook (via conduit) → nginx → the transport adapter in *some*
  instance; self-host = websocket straight into the *one* instance's receive loop. The dispatcher and
  everything below it live inside the app instance — that box is the convergence.
- **ACK is webhook-only.** Twitch retries an HTTP webhook until it gets a `2xx`, so the webhook controller
  returns **200 after the durable store** (store-then-ack, so a crash can't lose it). A WebSocket has no
  per-event ack — Twitch pushes `notification` frames and the receive loop just reads them.
- **Identical from the dispatcher down (X → Y → Z):** dedupe → **store** → **dispatch** 'it happened' on the
  bus → **route by type** to subscribers → **outcomes**. One event, fanned to every interested handler, none
  aware of the others.

## 2. Full SaaS topology — every entity, organized in four zones

Four zones, top to bottom: **Inbound → Load balancer → API instance (×N) → Shared tiers (data + external).**
The instances are **stateless compute** — every durable thing they share (the one Twitch conduit, Postgres,
Redis) lives in the shared zones, *not* inside any instance. That's why N servers run side by side: nginx
spreads requests across identical instances, and they all read/write the same shared tier and the same
conduit. Exactly one instance at a time *manages* the conduit (the provisioner singleton).

```mermaid
flowchart TB
  subgraph IN["Inbound"]
    direction LR
    CON["Twitch EventSub Conduit<br/>(one · shared by all instances)"]
    CLI["Clients<br/>dashboard · public pages"]
  end

  LB["Load balancer · no sticky sessions"]

  subgraph NODE["API instance × N · stateless compute (identical)"]
    direction TB
    EDGE["Edge tier<br/>webhook verify · dispatcher (dedupe) · REST/SignalR · authorize"]
    WRK["Worker tier<br/>fair scheduler · 3 lanes · engine"]
    PRJ["Projection runner<br/>folds read models"]
    EDGE --> WRK --> PRJ
  end

  subgraph DATA["Shared data tier"]
    direction LR
    PG[("Postgres primary<br/>CommandLog + EventJournal")]
    RPL[("Read replicas")]
    RDS[("Redis / Garnet<br/>bus · cache · locks")]
  end

  subgraph OUTX["External services"]
    direction LR
    HLX["Twitch Helix"]
    STR["Stripe"]
    ING["Spotify · Discord · YouTube"]
    TTSX["TTS · Azure/ElevenLabs"]
    KMS["KMS (KEK)"]
  end

  PROV["Conduit provisioner + sweeps<br/>cluster singleton · IRunOnceGuard"]

  CON -->|webhook| LB
  CLI <-->|REST + SignalR| LB
  LB --> EDGE

  EDGE -->|append to-do| PG
  WRK -->|claim · write journal| PG
  PRJ -->|read journal| PG
  PRJ -->|write read models| RPL
  EDGE -->|reads| RPL
  WRK -->|publish events| RDS
  RDS -->|deliver / live push| EDGE

  WRK --> OUTX
  PROV -->|create / reconcile conduit| HLX
```

## 3. What swaps for self-host (color-coded)

🟩 green = self-host only · 🟪 purple = SaaS only · ⬜ grey = shared core (same code both ways). The whole
compute path is identical; only the things below the spine differ. Self-host collapses the instance ×N into a
single process.

```mermaid
flowchart TB
  subgraph B1["1 · Ingress"]
    direction LR
    A_SH["single process<br/>EventSub via websocket"]:::sh
    A["requests + Twitch<br/>events enter"]:::core
    A_SA["load balancer + N instances<br/>EventSub via conduit/webhook"]:::sa
  end

  B["2 · Edge tier<br/>authenticate · write to-do · ACK fast"]:::core

  subgraph B3["3 · Durable to-do list"]
    direction LR
    C_SH["SQLite"]:::sh
    C["CommandLogEntry"]:::core
    C_SA["Postgres primary + read replicas"]:::sa
  end

  subgraph B4["4 · Worker tier"]
    direction LR
    D_SH["KEK: OS vault"]:::sh
    D["fair scheduler · 3 lanes<br/>engine"]:::core
    D_SA["KEK: KMS"]:::sa
  end

  E["5 · EventJournal<br/>source of truth (same DB as #3)"]:::core

  subgraph B6["6 · Event bus + cache"]
    direction LR
    F_SH["in-memory"]:::sh
    F["publish events"]:::core
    F_SA["Redis / Garnet"]:::sa
  end

  subgraph B7["7 · Outputs"]
    direction LR
    G_SH["chat via IRC"]:::sh
    G["read models · dashboard · chat"]:::core
    G_SA["chat via Helix send"]:::sa
  end

  A --> B --> C --> D --> E --> F --> G

  classDef core fill:#ececf3,stroke:#444,color:#111;
  classDef sa fill:#e7d9ff,stroke:#7a3cff,color:#111;
  classDef sh fill:#d9f2e6,stroke:#1f9d63,color:#111;
```

## 4. The flow, zoomed in (a redemption end to end)

```mermaid
sequenceDiagram
  participant V as Viewer (Twitch)
  participant T as EventSub (conduit/ws)
  participant E as Edge tier
  participant L as CommandLogEntry
  participant W as Worker
  participant J as EventJournal
  participant B as EventBus
  participant P as Projections
  participant D as Dashboard

  V->>T: redeems reward
  T->>E: notification
  E->>E: verify · dedupe · authorize
  E->>L: append CommandLogEntry → ACK 200 (webhook) / no ack (websocket)
  Note over E,T: synchronous hot path ends — one bounded INSERT
  W->>L: claim ready row (per-tenant fair, lane = standard)
  W->>W: re-check invariants · run reward pipeline
  W->>J: emit RewardRedeemedEvent — journaled in same tx as state write
  J->>P: projection folds from journal (checkpoint pull)
  J->>B: publish to bus (live notify)
  B->>D: SignalR push (activity feed)
  W->>V: chat reply (Helix on SaaS / IRC on self-host)
```

## 5. The three load-bearing properties

1. **Stateless, identical instances** — any instance serves any tenant; unique state is in Postgres/Redis,
   not in-process. This is what makes rolling deploys safe, conduit EventSub survive instance churn, and N
   servers share one conduit. (Self-host is the degenerate case: exactly one instance.)
2. **Hot path = one bounded append** — the edge writes a `CommandLogEntry` and ACKs; all real work is async on
   the worker tier, fair-scheduled per tenant across 3 lanes (critical chat/mod never shed, background first).
3. **`EventJournal` is the source of truth** — events are journaled in the *same transaction* as the state
   write; projections are pull/checkpoint (rebuildable); the bus is just live-notify. A bus outage loses
   nothing.
