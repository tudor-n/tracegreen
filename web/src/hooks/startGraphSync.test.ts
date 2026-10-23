import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DomainEvent, GraphSnapshot } from '@/api/client'
import type { Connection } from './graphSync'
import { type EventStream, startGraphSync } from './startGraphSync'

/** Stands in for the browser's EventSource: tests emit named messages and errors on it. */
class FakeStream implements EventStream {
  readonly url: string
  readyState = 0
  onerror: ((event: Event) => void) | null = null
  closed = false
  private readonly listeners = new Map<string, ((event: MessageEvent<string>) => void)[]>()

  constructor(url: string) {
    this.url = url
  }

  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
  }

  close() {
    this.closed = true
    this.readyState = 2
  }

  emit(type: string, data: unknown = {}) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data: JSON.stringify(data) } as MessageEvent<string>)
    }
  }

  /** `closed`: the browser gave up (as after a proxy 502); otherwise it is retrying by itself. */
  fail(closed: boolean) {
    this.readyState = closed ? 2 : 0
    this.onerror?.(new Event('error'))
  }
}

interface PendingLoad {
  signal: AbortSignal
  resolve(snapshot: GraphSnapshot | undefined): void
  reject(error: unknown): void
}

const snapshot = (lastSeq: number) => ({ lastSeq }) as unknown as GraphSnapshot

let streams: FakeStream[]
let loads: PendingLoad[]
let statuses: Connection[]
let hydrated: number[]
let appliedSeqs: number[]
let stop: () => void

const stream = () => streams[streams.length - 1]
const status = () => statuses[statuses.length - 1]
const flush = () => vi.advanceTimersByTimeAsync(0)

beforeEach(() => {
  vi.useFakeTimers()
  streams = []
  loads = []
  statuses = []
  hydrated = []
  appliedSeqs = []
  stop = startGraphSync(
    {
      hydrate: (s) => hydrated.push(s.lastSeq),
      applyEvent: (e: DomainEvent) => appliedSeqs.push(e.seq),
    },
    {
      pollMs: 5000,
      reconnectMs: 2000,
      onStatus: (s) => statuses.push(s),
      openStream: (url) => {
        const created = new FakeStream(url)
        streams.push(created)
        return created
      },
      loadSnapshot: (signal) => new Promise((resolve, reject) => loads.push({ signal, resolve, reject })),
    },
  )
})

afterEach(() => {
  stop()
  vi.useRealTimers()
})

/** Stream open and the first snapshot (at `lastSeq`) applied. */
async function goLive(lastSeq = 3) {
  stream().emit('hello')
  loads[loads.length - 1].resolve(snapshot(lastSeq))
  await flush()
}

describe('startGraphSync', () => {
  it('starts as connecting on /api/events and hydrates from the snapshot fetched after hello', async () => {
    expect(status()).toBe('connecting')
    expect(stream().url).toBe('/api/events')
    expect(loads).toHaveLength(0)

    await goLive(3)

    expect(hydrated).toEqual([3])
    expect(status()).toBe('live')
  })

  it('applies domain events from the stream', async () => {
    await goLive(3)

    stream().emit('node.created', { type: 'node.created', seq: 4 })
    stream().emit('audit.appended', { type: 'audit.appended', seq: 5 })

    expect(appliedSeqs).toEqual([4, 5])
  })

  it('polls while the stream is down and stops polling once it is live again', async () => {
    await goLive(3)

    stream().fail(false)
    expect(loads).toHaveLength(2)
    loads[1].resolve(snapshot(3))
    await flush()
    expect(status()).toBe('polling')

    await vi.advanceTimersByTimeAsync(5000)
    expect(loads).toHaveLength(3)
    loads[2].resolve(snapshot(4))
    await flush()

    stream().emit('hello')
    loads[3].resolve(snapshot(4))
    await flush()
    expect(status()).toBe('live')

    await vi.advanceTimersByTimeAsync(20_000)
    expect(loads).toHaveLength(4)
  })

  it('opens a new stream after reconnectMs when the browser gives up, and closes the old one', async () => {
    await goLive(3)

    stream().fail(true)
    await vi.advanceTimersByTimeAsync(1999)
    expect(streams).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(1)

    expect(streams).toHaveLength(2)
    expect(streams[0].closed).toBe(true)
  })

  it('shows offline at once when the API is down at page load', async () => {
    stream().fail(true)
    loads[0].reject(new TypeError('fetch failed'))
    await flush()

    expect(status()).toBe('offline')
  })

  it('fetches again when hello arrives while an older poll is in flight', async () => {
    await goLive(3)
    stream().fail(true)
    await vi.advanceTimersByTimeAsync(2000)
    stream().emit('hello')
    expect(loads).toHaveLength(2)

    loads[1].resolve(snapshot(3))
    await flush()

    expect(loads).toHaveLength(3)
    loads[2].resolve(snapshot(5))
    await flush()
    expect(hydrated).toEqual([3, 3, 5])
    expect(status()).toBe('live')
  })

  it('stopping closes the stream, aborts the snapshot request, clears timers and ignores late answers', async () => {
    stream().emit('hello')
    stop()

    expect(stream().closed).toBe(true)
    expect(loads[0].signal.aborted).toBe(true)
    loads[0].resolve(snapshot(3))
    await vi.advanceTimersByTimeAsync(20_000)
    expect(hydrated).toEqual([])
    expect(loads).toHaveLength(1)
    expect(streams).toHaveLength(1)
  })
})
