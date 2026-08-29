import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Client } from '../types'

export default function Clients() {
  const [clients, setClients] = useState<Client[] | null>(null)
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null)
  const [revealedSecret, setRevealedSecret] = useState<string | null>(null)

  const [clientId, setClientId] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [redirectUri, setRedirectUri] = useState('')

  const load = () => {
    api.clients().then(setClients)
  }
  useEffect(load, [])

  const show = (text: string, ok: boolean) => setMsg({ text, ok })

  const create = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      const { secret } = await api.createClient(clientId, displayName, [redirectUri])
      setRevealedSecret(secret)
      setClientId('')
      setDisplayName('')
      setRedirectUri('')
      load()
    } catch (err) {
      show(err instanceof api.ApiError ? err.message : 'Erreur', false)
    }
  }

  const rotate = async (id: string) => {
    try {
      const { secret } = await api.rotateSecret(id)
      setRevealedSecret(secret)
    } catch (err) {
      show(err instanceof api.ApiError ? err.message : 'Erreur', false)
    }
  }

  const remove = async (id: string) => {
    if (!confirm(`Supprimer l'application ${id} ?`)) return
    try {
      await api.deleteClient(id)
      show('Application supprimée', true)
      load()
    } catch (err) {
      show(err instanceof api.ApiError ? err.message : 'Erreur', false)
    }
  }

  if (!clients) return <p>Chargement…</p>

  return (
    <div>
      <h2>Applications OAuth</h2>
      {msg && <p className={msg.ok ? 'msg ok' : 'msg error'}>{msg.text}</p>}
      {revealedSecret && (
        <p className="secret-reveal">
          Secret (affiché une seule fois) : <code>{revealedSecret}</code>{' '}
          <button className="ghost small" onClick={() => setRevealedSecret(null)}>
            OK, noté
          </button>
        </p>
      )}

      <form className="inline-form" onSubmit={create}>
        <input placeholder="client_id" value={clientId} onChange={(e) => setClientId(e.target.value)} required />
        <input placeholder="Nom affiché" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        <input
          placeholder="Redirect URI"
          value={redirectUri}
          onChange={(e) => setRedirectUri(e.target.value)}
          required
        />
        <button type="submit">Créer</button>
      </form>

      <table>
        <thead>
          <tr>
            <th>client_id</th>
            <th>Nom</th>
            <th>Redirect URIs</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {clients.map((c) => (
            <tr key={c.clientId}>
              <td>
                <code>{c.clientId}</code>
              </td>
              <td>{c.displayName}</td>
              <td>{c.redirectUris.join(', ')}</td>
              <td>
                <button className="ghost small" onClick={() => rotate(c.clientId)}>
                  Régénérer le secret
                </button>
                <button className="danger small" onClick={() => remove(c.clientId)}>
                  Supprimer
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
