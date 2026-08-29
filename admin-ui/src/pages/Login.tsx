import { useState } from 'react'
import { api } from '../api'

function Mark() {
  return (
    <svg width="40" height="40" viewBox="0 0 24 24" fill="none" style={{ color: 'var(--accent)' }}>
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

export default function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      await api.login(email, password)
      onLoggedIn()
    } catch {
      setError('Email ou mot de passe incorrect.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-page">
      <form className="login-card" onSubmit={submit}>
        <div className="login-mark">
          <Mark />
        </div>
        <h1>Administration</h1>
        <p className="sub">ShopAuth — accès réservé</p>
        {error && <p className="error">{error}</p>}
        <label>
          Email
          <input value={email} onChange={(e) => setEmail(e.target.value)} type="email" required autoFocus />
        </label>
        <label>
          Mot de passe
          <input value={password} onChange={(e) => setPassword(e.target.value)} type="password" required />
        </label>
        <button type="submit" disabled={busy}>
          {busy ? 'Connexion…' : 'Se connecter'}
        </button>
      </form>
    </div>
  )
}
