import { useState } from 'react';
import { categoryNames, type Category, type EventGroup, type SeriesChoice } from '../api';
import GroupCard from './GroupCard';

/** Groups with a category filter; optionally split into sections. */
export default function GroupList({
  groups,
  onChoice,
  sections,
  empty,
}: {
  groups: EventGroup[];
  onChoice: (key: string, choice: SeriesChoice | null) => void;
  sections?: { title: string; filter: (g: EventGroup) => boolean }[];
  empty: string;
}) {
  const [category, setCategory] = useState<Category | null>(null);
  const categories = [...new Set(groups.map((g) => g.category))];
  const shown = groups.filter((g) => category === null || g.category === category);
  const parts = sections ?? [{ title: '', filter: () => true }];

  return (
    <>
      {categories.length > 1 && (
        <div className="mt-4 flex flex-wrap gap-2">
          {[null, ...categories].map((c) => (
            <button
              key={c ?? 'all'}
              onClick={() => setCategory(c)}
              className={`rounded-full border px-3 py-1.5 text-sm transition ${
                category === c ? 'border-accent bg-accent/15 text-accent' : 'border-line text-muted hover:text-fg'
              }`}
            >
              {c ? categoryNames[c] : 'Alt'}
            </button>
          ))}
        </div>
      )}

      {shown.length === 0 && <div className="mt-6 rounded-2xl bg-surface p-6 text-muted">{empty}</div>}

      {parts.map(({ title, filter }) => {
        const part = shown.filter(filter);
        if (part.length === 0) return null;
        return (
          <div key={title} className="mt-6">
            {title && <h2 className="mb-3 text-sm font-medium tracking-wide text-muted uppercase">{title}</h2>}
            <div className="space-y-4">
              {part.map((g) => (
                <GroupCard key={g.key} group={g} onChoice={(choice) => onChoice(g.key, choice)} />
              ))}
            </div>
          </div>
        );
      })}
    </>
  );
}
