import { http } from './http'
import type { UserDto, UserCreateDto, UserUpdateDto } from '@/types/users'

export async function getUsers(): Promise<UserDto[]> {
  const { data } = await http.get<UserDto[]>('/users')
  return data
}

export async function createUser(model: UserCreateDto): Promise<UserDto> {
  const { data } = await http.post<UserDto>('/users', model)
  return data
}

export async function updateUser(id: string, model: UserUpdateDto): Promise<void> {
  await http.put(`/users/${id}`, model)
}

export async function setUserPassword(id: string, password: string): Promise<void> {
  await http.post(`/users/${id}/password`, { password })
}

export async function deleteUser(id: string): Promise<void> {
  await http.delete(`/users/${id}`)
}
