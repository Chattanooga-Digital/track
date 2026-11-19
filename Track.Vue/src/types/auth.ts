export interface CurrentUserDto {
  username: string
  isAdministrator: boolean
  email: string | null
}

export interface AuthOptionsDto {
  passwordReset: boolean
}

export interface LoginDto {
  username: string
  password: string
}
