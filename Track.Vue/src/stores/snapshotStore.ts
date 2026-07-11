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

  async function start() {
    // Seed immediately so the UI is not blank until the first broadcast arrives.
    snapshot.value = await getCurrent()

    if (connection) return

    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/metrics')
      .withAutomaticReconnect()
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
    })

    await connection.start()
    connected.value = true
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
