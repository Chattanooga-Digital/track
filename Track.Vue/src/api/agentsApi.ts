import { http } from './http'

export async function deleteAgent(name: string): Promise<void> {
  await http.delete(`/agents/${encodeURIComponent(name)}`)
}

export async function approveAgent(name: string): Promise<void> {
  await http.post(`/agents/${encodeURIComponent(name)}/approve`)
}

export async function revokeAgent(name: string): Promise<void> {
  await http.post(`/agents/${encodeURIComponent(name)}/revoke`)
}
