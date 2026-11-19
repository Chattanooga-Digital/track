export type MailSecurity = 'Auto' | 'StartTls' | 'SslOnConnect' | 'None'

export interface EmailSettingsDto {
  configured: boolean
  enabled: boolean
  host: string
  port: number
  security: MailSecurity
  userName: string | null
  hasPassword: boolean
  fromName: string
  fromAddress: string
  siteUrl: string
}

export interface EmailSettingsEditDto {
  enabled: boolean
  host: string
  port: number
  security: MailSecurity
  userName: string | null
  /** Blank keeps the stored password. */
  password: string | null
  fromName: string
  fromAddress: string
  siteUrl: string
}

export interface EmailTestDto extends EmailSettingsEditDto {
  to: string
}

export interface EmailTestResultDto {
  success: boolean
  message: string
}
