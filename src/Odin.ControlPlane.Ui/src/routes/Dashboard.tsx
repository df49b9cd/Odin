import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import DataCard from "../components/DataCard";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";
import StatusBadge from "../components/StatusBadge";
import type { TaskQueueSummary, WorkflowExecutionInfo } from "../api/types";
import { workflowStatusTone } from "../utils/status";

const WORKFLOW_REFRESH_MS = 15_000;
const OVERVIEW_REFRESH_MS = 20_000;
const QUEUE_REFRESH_MS = 20_000;

function Dashboard() {
  const {
    data: overview,
    isLoading: overviewLoading,
    isError: overviewError,
    refetch: refetchOverview
  } = useQuery({
    queryKey: ["system-overview"],
    queryFn: api.getSystemOverview,
    refetchInterval: OVERVIEW_REFRESH_MS
  });

  const {
    data: runningWorkflows,
    isLoading: workflowsLoading,
    isError: workflowsError,
    refetch: refetchWorkflows
  } = useQuery({
    queryKey: ["workflows", { status: "Running", pageSize: 5 }],
    queryFn: () => api.listWorkflows({ status: "Running", pageSize: 5 }),
    refetchInterval: WORKFLOW_REFRESH_MS
  });

  const {
    data: queueSummary,
    isLoading: queuesLoading,
    isError: queuesError,
    refetch: refetchQueues
  } = useQuery({
    queryKey: ["task-queues", { limit: 5 }],
    queryFn: api.listTaskQueues,
    refetchInterval: QUEUE_REFRESH_MS
  });

  const topQueues = useMemo(() => {
    if (!queueSummary?.queues?.length) {
      return [];
    }

    return [...queueSummary.queues]
      .sort((a, b) => b.pendingTasks - a.pendingTasks)
      .slice(0, 5);
  }, [queueSummary]);

  return (
    <div className="page" aria-labelledby="dashboard-title">
      <h2 id="dashboard-title" className="sr-only">
        Control plane overview
      </h2>

      <section className="page-section" aria-label="Key metrics">
        {overviewLoading && !overview ? (
          <LoadingState message="Summarising system state..." />
        ) : overviewError || !overview ? (
          <ErrorState
            message="We were unable to retrieve the system overview."
            action={
              <button className="primary" type="button" onClick={() => refetchOverview()}>
                Try again
              </button>
            }
          />
        ) : (
          <div className="card-grid">
            <DataCard
              title="Namespaces"
              value={overview.namespaces}
              caption="Registered isolation domains"
            />
            <DataCard
              title="Workflows"
              value={overview.workflows}
              caption="Total executions tracked"
            />
            <DataCard
              title="Task Queues"
              value={overview.activeTaskQueues}
              caption="Active routing lanes"
            />
            <DataCard
              title="Worker Hosts"
              value={overview.activeWorkerHosts}
              caption="Heartbeat-active hosts"
            />
          </div>
        )}
      </section>

      <section className="page-section" aria-label="Running workflow executions">
        <div className="actions-row">
          <h2 style={{ margin: 0 }}>Running workflows</h2>
          <button className="primary" type="button" onClick={() => refetchWorkflows()}>
            Refresh list
          </button>
        </div>

        {workflowsLoading && !runningWorkflows ? (
          <LoadingState message="Loading running workflows..." />
        ) : workflowsError || !runningWorkflows ? (
          <ErrorState
            message="Unable to fetch running workflows."
            action={
              <button className="primary" type="button" onClick={() => refetchWorkflows()}>
                Try again
              </button>
            }
          />
        ) : runningWorkflows.workflows.length === 0 ? (
          <div className="empty-state">No active workflow executions detected.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Workflow</th>
                <th>Type</th>
                <th>Task Queue</th>
                <th>Status</th>
                <th>Started</th>
              </tr>
            </thead>
            <tbody>
              {runningWorkflows.workflows.map((execution: WorkflowExecutionInfo) => (
                <tr key={`${execution.workflowId}-${execution.runId}`}>
                  <td>
                    <Link to={`/workflows/${encodeURIComponent(execution.workflowId)}?runId=${encodeURIComponent(execution.runId)}`}>
                      {execution.workflowId}
                    </Link>
                  </td>
                  <td>{execution.workflowType}</td>
                  <td>{execution.taskQueue}</td>
                  <td>
                    <StatusBadge label={execution.status} tone={workflowStatusTone(execution.status)} />
                  </td>
                  <td>{new Date(execution.startTime).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="page-section" aria-label="Task queue pressure">
        <div className="actions-row">
          <h2 style={{ margin: 0 }}>Most saturated task queues</h2>
          <button className="primary" type="button" onClick={() => refetchQueues()}>
            Refresh queues
          </button>
        </div>

        {queuesLoading && !queueSummary ? (
          <LoadingState message="Collecting queue metrics..." />
        ) : queuesError || !queueSummary ? (
          <ErrorState
            message="Unable to load task queue depth."
            action={
              <button className="primary" type="button" onClick={() => refetchQueues()}>
                Try again
              </button>
            }
          />
        ) : topQueues.length === 0 ? (
          <div className="empty-state">No queues registered yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Queue Name</th>
                <th>Pending Tasks</th>
              </tr>
            </thead>
            <tbody>
              {topQueues.map((queue: TaskQueueSummary) => (
                <tr key={queue.queueName}>
                  <td>
                    <Link to={`/task-queues?highlight=${encodeURIComponent(queue.queueName)}`}>
                      {queue.queueName}
                    </Link>
                  </td>
                  <td>{queue.pendingTasks}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

export default Dashboard;
