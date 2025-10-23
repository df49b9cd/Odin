import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import type { WorkerHostSummary } from "../api/types";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";

const REFRESH_INTERVAL_MS = 25_000;

function Workers() {
  const {
    data,
    isLoading,
    isError,
    refetch
  } = useQuery({
    queryKey: ["workers", { scope: "list" }],
    queryFn: api.listWorkerHosts,
    refetchInterval: REFRESH_INTERVAL_MS
  });

  const workers = useMemo(() => data ?? [], [data]);

  return (
    <div className="page" aria-labelledby="workers-title">
      <div className="actions-row">
        <div>
          <h2 id="workers-title" style={{ marginBottom: 0 }}>
            Worker hosts
          </h2>
          <p style={{ margin: "0.25rem 0 0", color: "#64748b" }}>
            Track shard ownership and lease expiry timelines for active hosts.
          </p>
        </div>
        <button className="primary" type="button" onClick={() => refetch()}>
          Refresh
        </button>
      </div>

      <section className="page-section" aria-label="Worker host table">
        {isLoading && !data ? (
          <LoadingState message="Loading worker hosts..." />
        ) : isError || !data ? (
          <ErrorState
            message="Unable to load worker hosts."
            action={
              <button className="primary" type="button" onClick={() => refetch()}>
                Try again
              </button>
            }
          />
        ) : workers.length === 0 ? (
          <div className="empty-state">No worker hosts have active shard leases.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Host Identity</th>
                <th>Owned Shards</th>
                <th>Shard IDs</th>
                <th>Earliest Lease Expiry</th>
                <th>Latest Lease Expiry</th>
              </tr>
            </thead>
            <tbody>
              {workers.map((worker: WorkerHostSummary) => (
                <tr key={worker.hostIdentity}>
                  <td>{worker.hostIdentity}</td>
                  <td>{worker.ownedShards}</td>
                  <td>{worker.shardIds.join(", ")}</td>
                  <td>
                    {worker.earliestLeaseExpiry
                      ? new Date(worker.earliestLeaseExpiry).toLocaleString()
                      : "—"}
                  </td>
                  <td>
                    {worker.latestLeaseExpiry
                      ? new Date(worker.latestLeaseExpiry).toLocaleString()
                      : "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

export default Workers;
