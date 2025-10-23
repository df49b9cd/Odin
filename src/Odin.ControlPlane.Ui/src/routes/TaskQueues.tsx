import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { api } from "../api/client";
import type { TaskQueueSummary } from "../api/types";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";

const REFRESH_INTERVAL_MS = 20_000;

function TaskQueues() {
  const [searchParams] = useSearchParams();
  const highlightQueue = searchParams.get("highlight");

  const {
    data,
    isLoading,
    isError,
    refetch
  } = useQuery({
    queryKey: ["task-queues", { scope: "list" }],
    queryFn: api.listTaskQueues,
    refetchInterval: REFRESH_INTERVAL_MS
  });

  const queues = useMemo(() => data?.queues ?? [], [data]);
  const generatedAt = data?.generatedAt ? new Date(data.generatedAt).toLocaleTimeString() : undefined;

  return (
    <div className="page" aria-labelledby="task-queues-title">
      <div className="actions-row">
        <div>
          <h2 id="task-queues-title" style={{ marginBottom: 0 }}>
            Task queues
          </h2>
          <p style={{ margin: "0.25rem 0 0", color: "#64748b" }}>
            Inspect queue backlog, namespace distribution, and saturation.
          </p>
        </div>
        <button className="primary" type="button" onClick={() => refetch()}>
          Refresh queues
        </button>
      </div>

      <section className="page-section" aria-label="Task queue table">
        {isLoading && !data ? (
          <LoadingState message="Loading task queues..." />
        ) : isError || !data ? (
          <ErrorState
            message="Unable to retrieve task queues."
            action={
              <button className="primary" type="button" onClick={() => refetch()}>
                Try again
              </button>
            }
          />
        ) : queues.length === 0 ? (
          <div className="empty-state">No task queues registered yet.</div>
        ) : (
          <div>
            <table className="table">
              <thead>
                <tr>
                  <th>Queue</th>
                  <th>Pending Tasks</th>
                </tr>
              </thead>
              <tbody>
                {queues.map((queue: TaskQueueSummary) => (
                  <tr
                    key={queue.queueName}
                    style={
                      highlightQueue === queue.queueName
                        ? { backgroundColor: "#dbeafe" }
                        : undefined
                    }
                  >
                    <td>{queue.queueName}</td>
                    <td>{queue.pendingTasks}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {generatedAt ? (
              <p style={{ marginTop: "0.75rem", color: "#64748b", fontSize: "0.85rem" }}>
                Snapshot taken at {generatedAt}
              </p>
            ) : null}
          </div>
        )}
      </section>
    </div>
  );
}

export default TaskQueues;
