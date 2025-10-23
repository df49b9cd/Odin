interface DataCardProps {
  title: string;
  value: string | number;
  caption?: string;
}

function DataCard({ title, value, caption }: DataCardProps) {
  return (
    <div className="card" aria-live="polite">
      <span className="card-title">{title}</span>
      <span className="card-value">{value}</span>
      {caption ? <span className="card-caption">{caption}</span> : null}
    </div>
  );
}

export default DataCard;
