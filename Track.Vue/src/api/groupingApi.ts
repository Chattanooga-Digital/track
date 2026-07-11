import { http } from './http'

export async function getIdentityEnvVars(): Promise<string[]> {
  const { data } = await http.get<string[]>('/grouping/identity-env-vars')
  return data
}

export async function setIdentityEnvVars(names: string[]): Promise<string[]> {
  const { data } = await http.put<string[]>('/grouping/identity-env-vars', names)
  return data
}
