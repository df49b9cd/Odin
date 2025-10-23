import type {
  ApiError,
  ListNamespacesResponse,
  ListQueuesResponse,
  ListWorkflowsResponse,
  Namespace,
  QueueStats,
  SystemOverviewResponse,
  WorkerHostSummary,
  WorkflowExecution,
  WorkflowHistoryResponse
} from "./types";

const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? "/api/v1").replace(/\/$/, "");

type HttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

interface RequestOptions extends Omit<RequestInit, "method"> {
  method?: HttpMethod;
  parseJson?: boolean;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", headers, body, parseJson = true, ...rest } = options;
  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(headers ?? {})
    },
    body,
    ...rest
  });

  if (!response.ok) {
    let errorPayload: ApiError | undefined;
    if (parseJson) {
      try {
        errorPayload = (await response.json()) as ApiError;
      } catch (error) {
        console.warn("Failed to parse error response", error);
      }
    }

    const message = errorPayload?.message ?? response.statusText;
    const error = new Error(message);
    (error as Error & { status?: number; code?: string }).status = response.status;
    if (errorPayload?.code) {
      (error as Error & { status?: number; code?: string }).code = errorPayload.code;
    }

    throw error;
  }

  if (!parseJson) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export const api = {
  getSystemOverview: () => request<SystemOverviewResponse>("/overview"),
  listWorkflows: (params?: {
    namespaceId?: string;
    status?: string;
    pageSize?: number;
    pageToken?: string;
  }) => {
    const query = new URLSearchParams();
    if (params?.namespaceId) query.append("namespaceId", params.namespaceId);
    if (params?.status) query.append("status", params.status);
    if (params?.pageSize) query.append("pageSize", String(params.pageSize));
    if (params?.pageToken) query.append("pageToken", params.pageToken);

    const queryString = query.toString();
    const path = `/workflows${queryString ? `?${queryString}` : ""}`;
    return request<ListWorkflowsResponse>(path);
  },
  getWorkflowExecution: (workflowId: string, params?: { namespaceId?: string; runId?: string }) => {
    const query = new URLSearchParams();
    if (params?.namespaceId) query.append("namespaceId", params.namespaceId);
    if (params?.runId) query.append("runId", params.runId);
    const queryString = query.toString();
    return request<WorkflowExecution>(`/workflows/${encodeURIComponent(workflowId)}${queryString ? `?${queryString}` : ""}`);
  },
  getWorkflowHistory: (workflowId: string, params: { namespaceId: string; runId: string; maxEvents?: number }) => {
    const query = new URLSearchParams({
      namespaceId: params.namespaceId,
      runId: params.runId
    });
    if (params.maxEvents) {
      query.append("maxEvents", String(params.maxEvents));
    }
    const queryString = query.toString();
    return request<WorkflowHistoryResponse>(`/workflows/${encodeURIComponent(workflowId)}/history?${queryString}`);
  },
  listNamespaces: (params?: { pageSize?: number; pageToken?: string }) => {
    const query = new URLSearchParams();
    if (params?.pageSize) query.append("pageSize", String(params.pageSize));
    if (params?.pageToken) query.append("pageToken", params.pageToken);
    const queryString = query.toString();
    const path = `/namespaces${queryString ? `?${queryString}` : ""}`;
    return request<ListNamespacesResponse>(path);
  },
  listTaskQueues: () => request<ListQueuesResponse>("/tasks/queues"),
  getQueueStats: (queueName: string) => request<QueueStats>(`/tasks/queues/${encodeURIComponent(queueName)}/stats`),
  listWorkerHosts: () => request<WorkerHostSummary[]>("/workers"),
  startWorkflow: (payload: {
    namespaceId: string;
    workflowType: string;
    taskQueue: string;
    workflowId?: string;
    input?: string;
  }) => request<{ workflowId: string; runId: string }>("/workflows/start", {
    method: "POST",
    body: JSON.stringify(payload)
  })
};
