import { http } from './http'
import type { StackDto } from '@/types/customer'

export async function getStacks(): Promise<StackDto[]> {
  const { data } = await http.get<StackDto[]>('/stacks')
  return data
}

export async function setSharedStacks(stackNames: string[]): Promise<void> {
  await http.put('/stacks/shared', stackNames)
}
