import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Role, UserAccount } from '../types'
import { REASON_LABELS } from '../types'

export default function Users() {
  const [users, setUsers] = useState<UserAccount[] | null>(null)
  const [roles, setRoles] = useState<Role[]>([])
  const [expanded, setExpanded] = useState<string | null>(null)
  const [msg, setMsg] = useState<{ text: string; ok: boolean } | null>(null)

  const [newEmail, setNewEmail] = useState('')
  const [newPassword, setNewPassword] = useState('')

  const load = () => {
    api.users().then(setUsers)
    api.roles().then(setRoles)
  }
  useEffect(load, [])

  const show = (text: string, ok: boolean) => setMsg({ text, ok })
  const after = (text: string) => (err?: unknown) => {
    if (err) show(err instanceof api.ApiError ? err.message : 'Erreur', false)
    else show(text, true)
    load()
  }

  const createUser = async (e: React.FormEvent) => {
    e.preventDefault()
    try {
      await api.createUser(newEmail, newPassword)
      setNewEmail('')
      setNewPassword('')
      after('Compte créé')()
    } catch (err) {
      after('')(err)
    }
  }

  const toggleAdmin = async (u: UserAccount) => {
    const isAdmin = u.globalRoles.includes('admin')
    const next = isAdmin ? u.globalRoles.filter((r) => r !== 'admin') : [...u.globalRoles, 'admin']
    try {
      await api.setRoles(u.id, next)
      after(isAdmin ? 'Rôle admin retiré' : 'Rôle admin accordé')()
    } catch (err) {
      after('')(err)
    }
  }

  if (!users) return <p>Chargement…</p>

  return (
    <div>
      <h2>Comptes</h2>
      {msg && <p className={msg.ok ? 'msg ok' : 'msg error'}>{msg.text}</p>}

      <form className="inline-form" onSubmit={createUser}>
        <input placeholder="email@entreprise.com" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} type="email" required />
        <input placeholder="Mot de passe" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} type="password" required />
        <button type="submit">Créer un compte</button>
      </form>

      <table>
        <thead>
          <tr>
            <th>Email</th>
            <th>Admin</th>
            <th>Statut</th>
            <th>Rôles applicatifs</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {users.map((u) => (
            <>
              <tr key={u.id}>
                <td>{u.email}</td>
                <td>
                  <button className="ghost" onClick={() => toggleAdmin(u)}>
                    {u.globalRoles.includes('admin') ? 'Retirer admin' : 'Rendre admin'}
                  </button>
                </td>
                <td>
                  {u.isLockedOut && <span className="badge bad">Désactivé</span>}
                  {u.activeSuspension && (
                    <span className="badge warn">Suspendu ({REASON_LABELS[u.activeSuspension.reason]})</span>
                  )}
                  {!u.isLockedOut && !u.activeSuspension && <span className="badge ok">Actif</span>}
                </td>
                <td>{u.appRoles.map((r) => `${r.roleName}@${r.clientId}`).join(', ') || '—'}</td>
                <td>
                  <button className="ghost" onClick={() => setExpanded(expanded === u.id ? null : u.id)}>
                    {expanded === u.id ? 'Fermer' : 'Gérer'}
                  </button>
                </td>
              </tr>
              {expanded === u.id && (
                <tr>
                  <td colSpan={5}>
                    <UserDetails user={u} roles={roles} onChanged={after('Mis à jour')} />
                  </td>
                </tr>
              )}
            </>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function UserDetails({
  user,
  roles,
  onChanged,
}: {
  user: UserAccount
  roles: Role[]
  onChanged: (err?: unknown) => void
}) {
  const [clientId, setClientId] = useState('')
  const [roleName, setRoleName] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [startsAt, setStartsAt] = useState('')
  const [endsAt, setEndsAt] = useState('')
  const [reason, setReason] = useState(0)
  const [note, setNote] = useState('')

  const wrap = (fn: () => Promise<void>) => async () => {
    try {
      await fn()
      onChanged()
    } catch (err) {
      onChanged(err)
    }
  }

  return (
    <div className="details">
      <div className="details-block">
        <h4>Rôles applicatifs</h4>
        <ul>
          {user.appRoles.map((r) => (
            <li key={r.id}>
              {r.roleName} sur <code>{r.clientId}</code>{' '}
              <button className="ghost small" onClick={wrap(() => api.removeAppRole(user.id, r.id))}>
                Retirer
              </button>
            </li>
          ))}
          {user.appRoles.length === 0 && <li>Aucun</li>}
        </ul>
        <div className="inline-form">
          <input placeholder="client_id" value={clientId} onChange={(e) => setClientId(e.target.value)} />
          <select value={roleName} onChange={(e) => setRoleName(e.target.value)}>
            <option value="">Rôle…</option>
            {roles.map((r) => (
              <option key={r.id} value={r.name}>
                {r.name}
              </option>
            ))}
          </select>
          <button
            disabled={!clientId || !roleName}
            onClick={wrap(() => api.addAppRole(user.id, clientId, roleName))}
          >
            Ajouter
          </button>
        </div>
      </div>

      <div className="details-block">
        <h4>Suspension temporaire</h4>
        {user.activeSuspension ? (
          <p>
            En cours ({REASON_LABELS[user.activeSuspension.reason]}, jusqu'au{' '}
            {new Date(user.activeSuspension.endsAt).toLocaleString()}){' '}
            <button
              className="ghost small"
              onClick={wrap(() => api.removeSuspension(user.id, user.activeSuspension!.id))}
            >
              Lever
            </button>
          </p>
        ) : (
          <div className="inline-form">
            <input type="datetime-local" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
            <input type="datetime-local" value={endsAt} onChange={(e) => setEndsAt(e.target.value)} />
            <select value={reason} onChange={(e) => setReason(Number(e.target.value))}>
              {REASON_LABELS.map((label, i) => (
                <option key={i} value={i}>
                  {label}
                </option>
              ))}
            </select>
            <input placeholder="Note" value={note} onChange={(e) => setNote(e.target.value)} />
            <button
              disabled={!startsAt || !endsAt}
              onClick={wrap(() =>
                api.addSuspension(user.id, new Date(startsAt).toISOString(), new Date(endsAt).toISOString(), reason, note),
              )}
            >
              Suspendre
            </button>
          </div>
        )}
      </div>

      <div className="details-block">
        <h4>Autres actions</h4>
        <div className="inline-form">
          <input
            placeholder="Nouveau mot de passe"
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
          />
          <button disabled={!newPassword} onClick={wrap(() => api.resetPassword(user.id, newPassword))}>
            Réinitialiser
          </button>
          {user.isLockedOut ? (
            <button className="ghost" onClick={wrap(() => api.enableUser(user.id))}>
              Réactiver le compte
            </button>
          ) : (
            <button className="ghost" onClick={wrap(() => api.disableUser(user.id))}>
              Désactiver le compte
            </button>
          )}
          <button
            className="danger"
            onClick={() => {
              if (confirm(`Supprimer ${user.email} ?`)) wrap(() => api.deleteUser(user.id))()
            }}
          >
            Supprimer le compte
          </button>
        </div>
      </div>
    </div>
  )
}
