export default function Logo({ className = '' }: { className?: string }) {
  return (
    <span className={`inline-flex items-center gap-2 font-semibold tracking-tight ${className}`}>
      <svg viewBox="0 0 64 64" className="h-7 w-7" aria-hidden>
        <rect width="64" height="64" rx="14" className="fill-surface-2" />
        <path
          d="M14 20h36a2 2 0 0 1 2 2v5a5 5 0 0 0 0 10v5a2 2 0 0 1-2 2H14a2 2 0 0 1-2-2v-5a5 5 0 0 0 0-10v-5a2 2 0 0 1 2-2z"
          className="fill-accent"
        />
        <path d="M40 22v20" className="stroke-surface-2" strokeWidth="2.5" strokeDasharray="3 3" />
      </svg>
      Kultur
    </span>
  );
}
