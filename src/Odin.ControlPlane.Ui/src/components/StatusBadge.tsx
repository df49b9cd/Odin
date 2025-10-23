import clsx from "classnames";

type Tone = "success" | "warning" | "danger" | "info";

interface StatusBadgeProps {
  label: string;
  tone?: Tone;
}

function StatusBadge({ label, tone = "info" }: StatusBadgeProps) {
  return <span className={clsx("badge", tone)}>{label}</span>;
}

export default StatusBadge;
