import { http } from './http'
import type { UnidentifiedContainerDto } from '@/types/inventory'

export async function getInventory(): Promise<UnidentifiedContainerDto[]> {
  const { data } = await http.get<UnidentifiedContainerDto[]>('/inventory')
  return data
}
