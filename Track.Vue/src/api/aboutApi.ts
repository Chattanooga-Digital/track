import { http } from './http'

export interface AboutDto {
  version: string
}

export async function getAbout(): Promise<AboutDto> {
  const { data } = await http.get<AboutDto>('/about')
  return data
}
