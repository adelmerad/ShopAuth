export type Session = {
  id: string
  email: string
  roles: string[]
}

export type AppRole = {
  id: number
  clientId: string
  roleName: string
}

export type Suspension = {
  id: number
  startsAt: string
  endsAt: string
  reason: 0 | 1 | 2
  note: string | null
}

export type UserAccount = {
  id: string
  email: string
  isLockedOut: boolean
  globalRoles: string[]
  appRoles: AppRole[]
  activeSuspension: Suspension | null
}

export type Role = {
  id: string
  name: string
  protected: boolean
}

export type Client = {
  clientId: string
  displayName: string
  redirectUris: string[]
}

export const REASON_LABELS = ['Congé', 'Disciplinaire', 'Autre'] as const
