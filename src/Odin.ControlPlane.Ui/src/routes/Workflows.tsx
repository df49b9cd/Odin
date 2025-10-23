import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { ListNamespacesResponse, WorkflowExecutionInfo } from "../api/types";
import DataCard from "../components/DataCard";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";
import StatusBadge from "../components/StatusBadge";
import { workflowStatusTone } from "../utils/status";

const PAGE_SIZE = 25;
const AUTO_REFRESH_INTERVAL_MS = 15_000;
const STATUSES = [
  { label: "All", value: "all" },
  { label: "Running", value: "Running" },
  { label: "Completed", value: "Completed" },
  { label: "Failed", value: "Failed" },
  { label: "Terminated", value: "Terminated" },
  { label: "Timed out", value: "TimedOut" }
];

function Workflows() {
  const [selectedNamespace, setSelectedNamespace] = useState<string | undefined>(undefined);
  const [statusFilter, setStatusFilter] = useState<string>("all");
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [pageTokens, setPageTokens] = useState<string[]>(["0"]);

  const namespacesQuery = useQuery<ListNamespacesResponse>({
    queryKey: ["namespaces", { scope: "options" }],
    queryFn: () => api.listNamespaces({ pageSize: 100 })
  });

  const namespaces = useMemo(() => namespacesQuery.data?.namespaces ?? [], [namespacesQuery.data]);

  useEffect(() => {
    if (!selectedNamespace && namespaces.length > 0) {
      setSelectedNamespace(namespaces[0].namespaceId);
    }
  }, [namespaces, selectedNamespace]);

  useEffect(() => {
    setPageTokens(["0"]);
  }, [selectedNamespace, statusFilter]);

  const currentToken = pageTokens.at(-1) ?? "0";

  const workflowsQuery = useQuery({
    queryKey: [
      "workflows-list",
      {
        namespaceId: selectedNamespace,
        status: statusFilter,
        pageToken: currentToken
      }
    ],
    queryFn: () =>
      api.listWorkflows({
        namespaceId: selectedNamespace!,
        status: statusFilter === "all" ? undefined : statusFilter,
        pageSize: PAGE_SIZE,
        pageToken: currentToken === "0" ? undefined : currentToken
      }),
    enabled: Boolean(selectedNamespace),
    refetchInterval: autoRefresh ? AUTO_REFRESH_INTERVAL_MS : false
  });

  const workflowCount = workflowsQuery.data?.workflows.length ?? 0;
  const hasNextPage = Boolean(workflowsQuery.data?.nextPageToken);
  const hasPreviousPage = pageTokens.length > 1;

  const handleNextPage = () => {
    if (workflowsQuery.data?.nextPageToken) {
      setPageTokens((tokens) => [...tokens, workflowsQuery.data!.nextPageToken!]);
    }
  };

  const handlePreviousPage = () => {
    if (pageTokens.length > 1) {
      setPageTokens((tokens) => tokens.slice(0, -1));
    }
  };

  return (
    <div className="page" aria-labelledby="workflows-title">
      <div className="actions-row">
        <div>
          <h2 id="workflows-title" style={{ marginBottom: 0 }}>
            Workflow executions
          </h2>
          <p style={{ margin: "0.25rem 0 0", color: "#64748b" }}>
            Monitor workflow health, state transitions, and queue assignments.
          </p>
        </div>
        <div className="filters" role="group" aria-label="Workflow view controls">
          <label>
            Auto-refresh
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(event) => setAutoRefresh(event.target.checked)}
              style={{ marginLeft: "0.5rem" }}
            />
          </label>
          <button className="primary" type="button" onClick={() => workflowsQuery.refetch()}>
            Manual refresh
          </button>
        </div>
      </div>

      <section className="page-section" aria-label="Filters">
        <div className="filters">
          <label>
            Namespace
            <select
              value={selectedNamespace ?? ""}
              onChange={(event) => setSelectedNamespace(event.target.value)}
              style={{ marginLeft: "0.5rem", minWidth: "200px" }}
            >
              {namespaces.map((ns) => (
                <option key={ns.namespaceId} value={ns.namespaceId}>
                  {ns.namespaceName}
                </option>
              ))}
            </select>
          </label>
          <label>
            Status
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
              style={{ marginLeft: "0.5rem", minWidth: "160px" }}
            >
              {STATUSES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>
      </section>

      <section className="page-section" aria-label="Workflow executions table">
        {workflowsQuery.isLoading && !workflowsQuery.data ? (
          <LoadingState message="Loading workflows..." />
        ) : workflowsQuery.isError || !workflowsQuery.data ? (
          <ErrorState
            message="Unable to load workflows."
            action={
              <button className="primary" type="button" onClick={() => workflowsQuery.refetch()}>
                Try again
              </button>
            }
          />
        ) : workflowCount === 0 ? (
          <div className="empty-state">No workflows match the selected filters.</div>
        ) : (
          <div>
            <table className="table">
              <thead>
                <tr>
                  <th>Workflow</th>
                  <th>Run</th>
                  <th>Type</th>
                  <th>Task Queue</th>
                  <th>Status</th>
                  <th>Started</th>
                  <th>Duration</th>
                </tr>
              </thead>
              <tbody>
                {workflowsQuery.data.workflows.map((execution: WorkflowExecutionInfo) => (
                  <tr key={`${execution.workflowId}-${execution.runId}`}>
                    <td>
                      <Link to={`/workflows/${encodeURIComponent(execution.workflowId)}?runId=${encodeURIComponent(execution.runId)}`}>
                        {execution.workflowId}
                      </Link>
                    </td>
                    <td>{execution.runId}</td>
                    <td>{execution.workflowType}</td>
                    <td>{execution.taskQueue}</td>
                    <td>
                      <StatusBadge label={execution.status} tone={workflowStatusTone(execution.status)} />
                    </td>
                    <td>{new Date(execution.startTime).toLocaleString()}</td>
                    <td>
                      {execution.executionDuration
                        ? execution.executionDuration
                        : execution.closeTime
                          ? "--"
                          : "Running"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            <div className="actions-row" style={{ marginTop: "1rem" }}>
              <div className="filters">
                <button type="button" onClick={handlePreviousPage} disabled={!hasPreviousPage}>
                  Previous
                </button>
                <button type="button" onClick={handleNextPage} disabled={!hasNextPage}>
                  Next
                </button>
              </div>
              <span style={{ color: "#64748b", fontSize: "0.9rem" }}>
                Showing {workflowCount} workflows • Page token {currentToken || "0"}
              </span>
            </div>
          </div>
        )}
      </section>

      <section className="page-section" aria-label="Filter summary">
        <div className="card-grid">
          <DataCard title="Active filters" value={statusFilter === "all" ? "All" : statusFilter} caption="Status" />
          <DataCard
            title="Namespace"
            value={
              namespaces.find((ns) => ns.namespaceId === selectedNamespace)?.namespaceName ?? "--"
            }
            caption="Current scope"
          />
        </div>
      </section>
    </div>
  );
}

export default Workflows;
