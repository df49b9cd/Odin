import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useParams, useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import type {
  ListNamespacesResponse,
  WorkflowExecution,
  WorkflowHistoryEvent,
  WorkflowHistoryResponse
} from "../api/types";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";
import StatusBadge from "../components/StatusBadge";
import { workflowStatusTone } from "../utils/status";

function WorkflowDetail() {
  const { workflowId } = useParams();
  const [searchParams] = useSearchParams();
  const runIdParam = searchParams.get("runId") ?? undefined;
  const [selectedNamespace, setSelectedNamespace] = useState<string | undefined>(undefined);

  const namespacesQuery = useQuery<ListNamespacesResponse>({
    queryKey: ["namespaces", { scope: "workflow-detail" }],
    queryFn: () => api.listNamespaces({ pageSize: 100 })
  });

  const namespaces = useMemo(() => namespacesQuery.data?.namespaces ?? [], [namespacesQuery.data]);

  useEffect(() => {
    if (!selectedNamespace && namespaces.length > 0) {
      const defaultNamespace = namespaces.find((ns) => ns.namespaceName === "default") ?? namespaces[0];
      setSelectedNamespace(defaultNamespace.namespaceId);
    }
  }, [namespaces, selectedNamespace]);

  const workflowQuery = useQuery({
    queryKey: ["workflow", workflowId, selectedNamespace, runIdParam],
    queryFn: () =>
      api.getWorkflowExecution(workflowId!, {
        namespaceId: selectedNamespace,
        runId: runIdParam
      }),
    enabled: Boolean(workflowId && selectedNamespace)
  });

  const runId = runIdParam ?? workflowQuery.data?.runId;

  const historyQuery = useQuery({
    queryKey: ["workflow-history", workflowId, selectedNamespace, runId],
    queryFn: () =>
      api.getWorkflowHistory(workflowId!, {
        namespaceId: selectedNamespace!,
        runId: runId!,
        maxEvents: 200
      }),
    enabled: Boolean(workflowId && selectedNamespace && runId)
  });

  if (!workflowId) {
    return <ErrorState message="Workflow ID missing from route." />;
  }

  return (
    <div className="page" aria-labelledby="workflow-detail-title">
      <div className="actions-row">
        <div>
          <h2 id="workflow-detail-title" style={{ marginBottom: 0 }}>
            Workflow {workflowId}
          </h2>
          <p style={{ margin: "0.25rem 0 0", color: "#64748b" }}>
            Inspect execution metadata, lifecycle state, and recent history events.
          </p>
        </div>
        <div className="filters">
          <label>
            Namespace
            <select
              value={selectedNamespace ?? ""}
              onChange={(event) => setSelectedNamespace(event.target.value)}
              style={{ marginLeft: "0.5rem", minWidth: "220px" }}
            >
              {namespaces.map((ns) => (
                <option key={ns.namespaceId} value={ns.namespaceId}>
                  {ns.namespaceName}
                </option>
              ))}
            </select>
          </label>
          <button className="primary" type="button" onClick={() => workflowQuery.refetch()}>
            Refresh
          </button>
        </div>
      </div>

      <section className="page-section" aria-label="Workflow metadata">
        {workflowQuery.isLoading && !workflowQuery.data ? (
          <LoadingState message="Loading workflow details..." />
        ) : workflowQuery.isError || !workflowQuery.data ? (
          <ErrorState
            message="Unable to load workflow details."
            action={
              <button className="primary" type="button" onClick={() => workflowQuery.refetch()}>
                Try again
              </button>
            }
          />
        ) : (
          <div className="detail-grid">
            <div className="card">
              <span className="card-title">Run ID</span>
              <span className="card-value" style={{ fontSize: "1rem" }}>
                {workflowQuery.data.runId}
              </span>
            </div>
            <div className="card">
              <span className="card-title">Workflow type</span>
              <span className="card-value" style={{ fontSize: "1.1rem" }}>
                {workflowQuery.data.workflowType}
              </span>
              <span className="card-caption">Task queue: {workflowQuery.data.taskQueue}</span>
            </div>
            <div className="card">
              <span className="card-title">Status</span>
              <StatusBadge
                label={workflowQuery.data.workflowState}
                tone={workflowStatusTone(workflowQuery.data.workflowState)}
              />
              <span className="card-caption">
                Updated {new Date(workflowQuery.data.lastUpdatedAt).toLocaleString()}
              </span>
            </div>
            <div className="card">
              <span className="card-title">Timing</span>
              <span className="card-caption">
                Started {new Date(workflowQuery.data.startedAt).toLocaleString()}
              </span>
              <span className="card-caption">
                {workflowQuery.data.completedAt
                  ? `Completed ${new Date(workflowQuery.data.completedAt).toLocaleString()}`
                  : "Still running"}
              </span>
            </div>
          </div>
        )}
      </section>

      <section className="page-section" aria-label="Workflow history">
        {historyQuery.isLoading && !historyQuery.data ? (
          <LoadingState message="Loading execution history..." />
        ) : historyQuery.isError || !historyQuery.data ? (
          <ErrorState
            message="Unable to load workflow history."
            action={
              <button className="primary" type="button" onClick={() => historyQuery.refetch()}>
                Retry
              </button>
            }
          />
        ) : historyQuery.data.events.length === 0 ? (
          <div className="empty-state">No execution history events available.</div>
        ) : (
          <div className="history-list" role="list">
            {historyQuery.data.events.map((event: WorkflowHistoryEvent) => (
              <div className="history-item" key={event.eventId} role="listitem">
                <div className="history-item-header">
                  <span>
                    #{event.eventId} &mdash; {event.eventType}
                  </span>
                  <span>{new Date(event.eventTimestamp).toLocaleString()}</span>
                </div>
                <div className="history-item-body">
                  <small style={{ color: "#94a3b8" }}>
                    Task #{event.taskId} • Version {event.version}
                  </small>
                  <pre
                    style={{
                      margin: "0.4rem 0 0",
                      whiteSpace: "pre-wrap",
                      wordBreak: "break-word",
                      fontFamily: "Menlo, Monaco, Consolas, monospace",
                      fontSize: "0.85rem"
                    }}
                  >
                    {JSON.stringify(event.eventData, null, 2)}
                  </pre>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

export default WorkflowDetail;
