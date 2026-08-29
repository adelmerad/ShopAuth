import { useEffect, useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { api } from './api'
import Login from './pages/Login'
import Users from './pages/Users'
import Clients from './pages/Clients'
import Roles from './pages/Roles'
import type { Session } from './types'

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

  return (
    <div className="shell">
      <nav>
        <div className="brand">Administration</div>
        <NavLink to="/users">Comptes</NavLink>
        <NavLink to="/clients">Applications</NavLink>
        <NavLink to="/roles">Rôles</NavLink>
        <div className="spacer" />
        <span className="who">{session.email}</span>
        <button className="ghost" onClick={() => api.logout().then(refreshSession)}>
          Déconnexion
        </button>
      </nav>
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
