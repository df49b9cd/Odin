import type { WorkflowStatus } from "../api/types";

type Tone = "success" | "warning" | "danger" | "info";

export function workflowStatusTone(status: WorkflowStatus): Tone {
  switch (status) {
    case "Running":
      return "info";
    case "Completed":
      return "success";
    case "Failed":
    case "Terminated":
    case "TimedOut":
      return "danger";
    case "Canceled":
    case "ContinuedAsNew":
      return "warning";
    default:
      return "info";
  }
}
