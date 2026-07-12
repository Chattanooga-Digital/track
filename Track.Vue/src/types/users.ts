export interface UserDto {
  id: string
  username: string
  email: string | null
  displayName: string | null
  isAdministrator: boolean
  disabled: boolean
  lockedOut: boolean
  createdAt: string
}

export interface UserCreateDto {
  username: string
  password: string
  email: string | null
  displayName: string | null
  isAdministrator: boolean
}

export interface UserUpdateDto {
  email: string | null
  displayName: string | null
  isAdministrator: boolean
  disabled: boolean
}
