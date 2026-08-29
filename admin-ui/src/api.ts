class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    credentials: 'include',
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
    ...init,
  })

  if (!res.ok) {
    const text = await res.text().catch(() => '')
    throw new ApiError(res.status, text || `Erreur ${res.status}`)
  }

  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  ApiError,

  login: (email: string, password: string) =>
    request<void>('/api/account/login', { method: 'POST', body: JSON.stringify({ email, password }) }),
  logout: () => request<void>('/api/account/logout', { method: 'POST' }),
  session: () => request<{ id: string; email: string; roles: string[] }>('/api/account/session'),

  users: () => request<import('./types').UserAccount[]>('/admin/api/users/'),
  createUser: (email: string, password: string) =>
    request<void>('/admin/api/users/', { method: 'POST', body: JSON.stringify({ email, password }) }),
  setRoles: (id: string, roles: string[]) =>
    request<void>(`/admin/api/users/${id}/roles`, { method: 'PUT', body: JSON.stringify({ roles }) }),
  addAppRole: (id: string, clientId: string, roleName: string) =>
    request<void>(`/admin/api/users/${id}/app-roles`, { method: 'POST', body: JSON.stringify({ clientId, roleName }) }),
  removeAppRole: (id: string, appRoleId: number) =>
    request<void>(`/admin/api/users/${id}/app-roles/${appRoleId}`, { method: 'DELETE' }),
  resetPassword: (id: string, newPassword: string) =>
    request<void>(`/admin/api/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  disableUser: (id: string) => request<void>(`/admin/api/users/${id}/disable`, { method: 'POST' }),
  enableUser: (id: string) => request<void>(`/admin/api/users/${id}/enable`, { method: 'POST' }),
  addSuspension: (id: string, startsAt: string, endsAt: string, reason: number, note: string) =>
    request<void>(`/admin/api/users/${id}/suspensions`, {
      method: 'POST',
      body: JSON.stringify({ startsAt, endsAt, reason, note }),
    }),
  removeSuspension: (id: string, suspensionId: number) =>
    request<void>(`/admin/api/users/${id}/suspensions/${suspensionId}`, { method: 'DELETE' }),
  deleteUser: (id: string) => request<void>(`/admin/api/users/${id}`, { method: 'DELETE' }),

  roles: () => request<import('./types').Role[]>('/admin/api/roles/'),
  createRole: (name: string) => request<void>('/admin/api/roles/', { method: 'POST', body: JSON.stringify({ name }) }),
  deleteRole: (id: string) => request<void>(`/admin/api/roles/${id}`, { method: 'DELETE' }),

  clients: () => request<import('./types').Client[]>('/admin/api/clients/'),
  createClient: (clientId: string, displayName: string, redirectUris: string[]) =>
    request<{ secret: string }>('/admin/api/clients/', {
      method: 'POST',
      body: JSON.stringify({ clientId, displayName, redirectUris }),
    }),
  updateClient: (clientId: string, displayName: string, redirectUris: string[]) =>
    request<void>(`/admin/api/clients/${clientId}`, {
      method: 'PUT',
      body: JSON.stringify({ displayName, redirectUris }),
    }),
  rotateSecret: (clientId: string) =>
    request<{ secret: string }>(`/admin/api/clients/${clientId}/rotate-secret`, { method: 'POST' }),
  deleteClient: (clientId: string) => request<void>(`/admin/api/clients/${clientId}`, { method: 'DELETE' }),
}
