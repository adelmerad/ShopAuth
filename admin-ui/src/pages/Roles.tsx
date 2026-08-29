import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Role } from '../types'

export default function Roles() {
  const [roles, setRoles] = useState<Role[] | null>(null)
  const [name, setName] = useState('')
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null)

  const load = () => {
    api.roles().then(setRoles)
  }
  useEffect(load, [])

  const create = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      await api.createRole(name)
      setName('')
      setMsg({ text: 'Rôle créé', ok: true })
      load()
    } catch (err) {
      setMsg({ text: err instanceof api.ApiError ? err.message : 'Erreur', ok: false })
    }
  }

  const remove = async (r: Role) => {
    if (!confirm(`Supprimer le rôle ${r.name} ?`)) return
    try {
      await api.deleteRole(r.id)
      load()
    } catch (err) {
      setMsg({ text: err instanceof api.ApiError ? err.message : 'Erreur', ok: false })
    }
  }

  if (!roles) return <p>Chargement…</p>

  return (
    <div>
      <h2>Rôles</h2>
      {msg && <p className={msg.ok ? 'msg ok' : 'msg error'}>{msg.text}</p>}

      <form className="inline-form" onSubmit={create}>
        <input placeholder="Nom du rôle" value={name} onChange={(e) => setName(e.target.value)} required />
        <button type="submit">Créer</button>
      </form>

      <table>
        <thead>
          <tr>
            <th>Nom</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {roles.map((r) => (
            <tr key={r.id}>
              <td>
                {r.name} {r.protected && <span className="badge">protégé</span>}
              </td>
              <td>
                {!r.protected && (
                  <button className="danger small" onClick={() => remove(r)}>
                    Supprimer
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
