import { useState, type FormEvent } from 'react';
import { loginErrorMessage, useAuth } from '../auth';
import Logo from '../components/Logo';

export default function Login() {
  const { login } = useAuth();
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(password);
    } catch (err) {
      setError(loginErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid h-full place-items-center px-4">
      <div className="w-full max-w-sm rounded-2xl bg-surface p-8 shadow-xl">
        <Logo className="mb-6 text-xl" />
        <form onSubmit={submit}>
          <label htmlFor="password" className="mb-2 block text-sm text-muted">
            Kodeord
          </label>
          <input
            id="password"
            type="password"
            autoFocus
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="w-full rounded-lg border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent"
          />
          {error && <p className="mt-3 text-sm text-danger">{error}</p>}
          <button
            type="submit"
            disabled={busy || !password}
            className="mt-6 w-full rounded-lg bg-accent py-2.5 font-medium text-on-accent transition hover:bg-accent-strong disabled:opacity-50"
          >
            {busy ? 'Logger ind…' : 'Log ind'}
          </button>
        </form>
      </div>
    </div>
  );
}
