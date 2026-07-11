import { http } from './http'

export async function deleteAgent(name: string): Promise<void> {
  await http.delete(`/agents/${encodeURIComponent(name)}`)
}
