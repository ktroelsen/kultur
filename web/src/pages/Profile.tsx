import { useEffect, useState, type FormEvent } from 'react';
import { api, errorMessage, type ScanResult, type TasteProfile } from '../api';

const field = 'mt-2 w-full rounded-lg border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent';

// "Det tror jeg om dig": the taste profile in plain words. For now you write it yourself;
// later Claude keeps it up to date from what you save and from Spotify – and you can always correct it here.
export default function Profile() {
  const [profile, setProfile] = useState<TasteProfile | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [scan, setScan] = useState<ScanResult | null>(null);
  const [scanning, setScanning] = useState(false);

  useEffect(() => {
    api<TasteProfile>('/api/profile').then(setProfile).catch((err) => setStatus(errorMessage(err)));
  }, []);

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!profile) return;
    setStatus(null);
    try {
      setProfile(await api<TasteProfile>('/api/profile', { method: 'PUT', json: profile }));
      setStatus('Gemt.');
    } catch (err) {
      setStatus(errorMessage(err, 'Kunne ikke gemme.'));
    }
  }

  async function scanNow() {
    setScanning(true);
    setScan(null);
    try {
      setScan(await api<ScanResult>('/api/scan', { method: 'POST' }));
    } catch (err) {
      setStatus(errorMessage(err, 'Scanningen fejlede.'));
    } finally {
      setScanning(false);
    }
  }

  return (
    <section>
      <h1 className="text-2xl font-semibold">Det tror jeg om dig</h1>
      <p className="mt-1 text-sm text-muted">
        Skriv med dine egne ord, hvad du kan lide. Senere lærer jeg af det, du gemmer, og af din Spotify.
      </p>

      {profile && (
        <form onSubmit={save} className="mt-6 space-y-5">
          <label className="block">
            <span className="text-sm text-muted">Kultur – kunstnere, genrer, steder, stemning</span>
            <textarea
              rows={5}
              value={profile.about}
              onChange={(e) => setProfile({ ...profile, about: e.target.value })}
              placeholder="Fx singer-songwriter, ny dansk dramatik, stand-up på hverdage. Ikke stadionkoncerter."
              className={field}
            />
          </label>
          <label className="block">
            <span className="text-sm text-muted">Sport – hold, sportsgrene og slags begivenheder</span>
            <textarea
              rows={4}
              value={profile.sport}
              onChange={(e) => setProfile({ ...profile, sport: e.target.value })}
              placeholder="Fx Vejle Boldklub, landsholdene i fodbold, håndbold og badminton. Slutrunder og finaler."
              className={field}
            />
          </label>
          <div className="flex items-center gap-3">
            <button type="submit" className="rounded-lg bg-accent px-4 py-2.5 font-medium text-on-accent hover:bg-accent-strong">
              Gem
            </button>
            {status && <span className="text-sm text-muted">{status}</span>}
          </div>
        </form>
      )}

      <div className="mt-10 rounded-2xl bg-surface p-5">
        <h2 className="font-semibold">Kilder</h2>
        <p className="mt-1 text-sm text-muted">Jeg leder efter nye arrangementer hver nat. Du kan også gøre det nu.</p>
        <button
          onClick={scanNow}
          disabled={scanning}
          className="mt-4 rounded-lg border border-line px-4 py-2 text-sm hover:border-accent disabled:opacity-50"
        >
          {scanning ? 'Leder…' : 'Led efter nyt nu'}
        </button>
        {scan && (
          <ul className="mt-4 space-y-1 text-sm">
            {scan.sources.map((s) => (
              <li key={s.source}>
                <span className="font-medium capitalize">{s.source}</span>:{' '}
                {s.error ? <span className="text-danger">{s.error}</span> : <span className="text-muted">{s.fetched} fundet</span>}
              </li>
            ))}
            <li className="text-muted">
              {scan.added} nye · {scan.total} i alt
            </li>
          </ul>
        )}
      </div>
    </section>
  );
}
