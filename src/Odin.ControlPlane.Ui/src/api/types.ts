export interface Namespace {
  namespaceId: string;
  namespaceName: string;
  description?: string | null;
  ownerId?: string | null;
  retentionDays: number;
  historyArchivalEnabled: boolean;
  visibilityArchivalEnabled: boolean;
  isGlobalNamespace: boolean;
  createdAt: string;
  updatedAt: string;
  status: "Active" | "Deprecated" | "Deleted";
}

export interface ListNamespacesResponse {
  namespaces: Namespace[];
  nextPageToken?: string | null;
}

export type WorkflowStatus =
  | "Unspecified"
  | "Running"
  | "Completed"
  | "Failed"
  | "Canceled"
  | "Terminated"
  | "ContinuedAsNew"
  | "TimedOut";

export interface WorkflowExecutionInfo {
  workflowId: string;
  runId: string;
  workflowType: string;
  taskQueue: string;
  status: WorkflowStatus;
  startTime: string;
  closeTime?: string | null;
  executionDuration?: string | null;
  historyLength: number;
  parentExecution?: {
    namespace: string;
    workflowId: string;
    runId: string;
  } | null;
  searchAttributes?: Record<string, unknown> | null;
  memo?: Record<string, unknown> | null;
}

export interface ListWorkflowsResponse {
  workflows: WorkflowExecutionInfo[];
  nextPageToken?: string | null;
}

export interface WorkflowExecution extends WorkflowExecutionInfo {
  namespaceId: string;
  workflowState: "Running" | "Completed" | "Failed" | "Canceled" | "Terminated" | "ContinuedAsNew" | "TimedOut";
  startedAt: string;
  completedAt?: string | null;
  lastUpdatedAt: string;
  shardId: number;
  version: number;
}

export interface ApiError {
  message: string;
  code?: string;
}

export interface QueueStats {
  queueName: string;
  pendingTasks: number;
  lastPolledAt?: string | null;
}

export interface TaskQueueSummary {
  queueName: string;
  pendingTasks: number;
}

export interface ListQueuesResponse {
  queues: TaskQueueSummary[];
  generatedAt: string;
}

export interface WorkerHostSummary {
  hostIdentity: string;
  ownedShards: number;
  shardIds: number[];
  earliestLeaseExpiry?: string | null;
  latestLeaseExpiry?: string | null;
}

export interface SystemOverviewResponse {
  namespaces: number;
  workflows: number;
  activeTaskQueues: number;
  activeWorkerHosts: number;
  generatedAt: string;
}

export interface WorkflowHistoryEvent {
  eventId: number;
  eventType: string;
  eventTimestamp: string;
  taskId: number;
  version: number;
  eventData: unknown;
}

export interface WorkflowHistoryResponse {
  namespaceId: string;
  workflowId: string;
  runId: string;
  events: WorkflowHistoryEvent[];
  firstEventId: number;
  lastEventId: number;
  nextPageToken?: string | null;
}
