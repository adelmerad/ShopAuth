import { useEffect, useState } from 'react'
import { api } from '../api'
import ActionsMenu from '../components/ActionsMenu'
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
      <div className="page-header">
        <h2>
          Applications
          <span className="count">{clients.length}</span>
        </h2>
      </div>

      {msg && <p className={msg.ok ? 'msg ok' : 'msg error'}>{msg.text}</p>}
      {revealedSecret && (
        <div className="secret-reveal">
          <span>Secret (affiché une seule fois) :</span>
          <code>{revealedSecret}</code>
          <button className="ghost small" onClick={() => setRevealedSecret(null)}>
            OK, noté
          </button>
        </div>
      )}

      <div className="card">
        <div className="eyebrow">Nouvelle application</div>
        <form className="inline-form" onSubmit={create} style={{ margin: 0 }}>
          <input placeholder="client_id" value={clientId} onChange={(e) => setClientId(e.target.value)} required />
          <input placeholder="Nom affiché" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
          <input placeholder="Redirect URI" value={redirectUri} onChange={(e) => setRedirectUri(e.target.value)} required />
          <button className="primary" type="submit">
            Créer
          </button>
        </form>
      </div>

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
              <td className="mono">{c.clientId}</td>
              <td>{c.displayName}</td>
              <td className="mono" style={{ color: 'var(--muted)' }}>
                {c.redirectUris.join(', ')}
              </td>
              <td>
                <ActionsMenu
                  items={[
                    { label: 'Régénérer le secret', onClick: () => rotate(c.clientId) },
                    { label: 'Supprimer', variant: 'danger', onClick: () => remove(c.clientId) },
                  ]}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
