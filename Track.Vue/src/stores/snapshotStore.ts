import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { SnapshotDto } from '@/types/stats'
import { getCurrent } from '@/api/statsApi'

export const useSnapshotStore = defineStore('snapshot', () => {
  const snapshot = ref<SnapshotDto | null>(null)
  const connected = ref(false)
  let connection: signalR.HubConnection | null = null

  const nodes = computed(() => snapshot.value?.nodes ?? [])
  const customers = computed(() => snapshot.value?.customers ?? [])
  const offlineNodes = computed(() => nodes.value.filter((n) => !n.online))

  let stopped = false

  async function start() {
    stopped = false
    // Seed immediately so the UI is not blank until the first broadcast arrives.
    snapshot.value = await getCurrent()

    if (connection) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/metrics')
      // Retry forever; the default policy gives up after ~30s and leaves the UI stuck on "Reconnecting".
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: () => 5000,
      })
      .build()

    connection.on('snapshot', (payload: SnapshotDto) => {
      snapshot.value = payload
    })

    connection.onreconnected(() => {
      connected.value = true
    })
    connection.onreconnecting(() => {
      connected.value = false
    })
    connection.onclose(() => {
      connected.value = false
      connection = null
      if (!stopped) void restart()
    })

    try {
      await connection.start()
      connected.value = true
    } catch {
      // onclose does not fire for a failed initial handshake, so restart is scheduled here instead.
      connection = null
      connected.value = false
      if (!stopped) void restart()
    }
  }

  async function restart() {
    await new Promise((resolve) => setTimeout(resolve, 5000))
    if (stopped || connection) return
    await start()
  }

  async function stop() {
    if (connection) {
      await connection.stop()
      connection = null
    }
    connected.value = false
  }

  return {
    snapshot,
    connected,
    nodes,
    customers,
    offlineNodes,
    start,
    stop,
  }
})
