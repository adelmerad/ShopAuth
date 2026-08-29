import { useEffect, useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { api } from './api'
import Login from './pages/Login'
import Users from './pages/Users'
import Clients from './pages/Clients'
import Roles from './pages/Roles'
import type { Session } from './types'

function BrandMark({ size = 30 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" style={{ color: 'var(--accent)' }}>
      <path
        d="M12 2.4 4.5 5.2v5.6c0 5.1 3.2 9.4 7.5 10.6 4.3-1.2 7.5-5.5 7.5-10.6V5.2L12 2.4Z"
        stroke="currentColor"
        strokeWidth="1.4"
        strokeLinejoin="round"
      />
      <circle cx="12" cy="10.6" r="2" fill="currentColor" />
      <path d="M12 12.6v2.6" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  )
}

const icons = {
  users: (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <circle cx="8" cy="5.4" r="2.5" stroke="currentColor" strokeWidth="1.3" />
      <path d="M3 14c0-2.76 2.24-5 5-5s5 2.24 5 5" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" />
    </svg>
  ),
  apps: (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <rect x="2" y="2" width="5" height="5" rx="1.2" stroke="currentColor" strokeWidth="1.3" />
      <rect x="9" y="2" width="5" height="5" rx="1.2" stroke="currentColor" strokeWidth="1.3" />
      <rect x="2" y="9" width="5" height="5" rx="1.2" stroke="currentColor" strokeWidth="1.3" />
      <rect x="9" y="9" width="5" height="5" rx="1.2" stroke="currentColor" strokeWidth="1.3" />
    </svg>
  ),
  tag: (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
      <path d="M2 2h5.5L14 8.5 8.5 14 2 7.5V2Z" stroke="currentColor" strokeWidth="1.3" strokeLinejoin="round" />
      <circle cx="5" cy="5" r="1" fill="currentColor" />
    </svg>
  ),
}

export default function App() {
  const [session, setSession] = useState<Session | null | 'loading'>('loading')

  const refreshSession = () => {
    api
      .session()
      .then(setSession)
      .catch(() => setSession(null))
  }

  useEffect(refreshSession, [])

  if (session === 'loading') return null

  if (!session) return <Login onLoggedIn={refreshSession} />

  if (!session.roles.includes('admin')) {
    return (
      <div className="denied">
        <p>Ce compte n'a pas le rôle admin.</p>
        <button onClick={() => api.logout().then(refreshSession)}>Se déconnecter</button>
      </div>
    )
  }

  const initial = session.email.charAt(0).toUpperCase()

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <BrandMark />
          <div className="brand-text">
            ShopAuth
            <small>Administration</small>
          </div>
        </div>
        <nav>
          <NavLink to="/users">
            {icons.users} Comptes
          </NavLink>
          <NavLink to="/clients">
            {icons.apps} Applications
          </NavLink>
          <NavLink to="/roles">
            {icons.tag} Rôles
          </NavLink>
        </nav>
        <div className="sidebar-footer">
          <div className="sidebar-user">
            <div className="sidebar-avatar">{initial}</div>
            <div className="sidebar-email">{session.email}</div>
          </div>
          <button className="ghost" onClick={() => api.logout().then(refreshSession)}>
            Déconnexion
          </button>
        </div>
      </aside>
      <main>
        <Routes>
          <Route path="/" element={<Users />} />
          <Route path="/users" element={<Users />} />
          <Route path="/clients" element={<Clients />} />
          <Route path="/roles" element={<Roles />} />
        </Routes>
      </main>
    </div>
  )
}
