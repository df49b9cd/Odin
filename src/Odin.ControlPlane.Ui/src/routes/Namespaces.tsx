import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import type { Namespace } from "../api/types";
import ErrorState from "../components/ErrorState";
import LoadingState from "../components/LoadingState";

function Namespaces() {
  const {
    data,
    isLoading,
    isError,
    refetch
  } = useQuery({
    queryKey: ["namespaces", { scope: "table" }],
    queryFn: () => api.listNamespaces({ pageSize: 200 })
  });

  const namespaces = useMemo(() => data?.namespaces ?? [], [data]);

  return (
    <div className="page" aria-labelledby="namespaces-title">
      <div className="actions-row">
        <div>
          <h2 id="namespaces-title" style={{ marginBottom: 0 }}>
            Namespaces
          </h2>
          <p style={{ margin: "0.25rem 0 0", color: "#64748b" }}>
            Manage multi-tenant isolation boundaries and retention settings.
          </p>
        </div>
        <button className="primary" type="button" onClick={() => refetch()}>
          Refresh
        </button>
      </div>

      <section className="page-section" aria-label="Namespace list">
        {isLoading && !data ? (
          <LoadingState message="Loading namespaces..." />
        ) : isError || !data ? (
          <ErrorState
            message="Unable to retrieve namespaces."
            action={
              <button className="primary" type="button" onClick={() => refetch()}>
                Try again
              </button>
            }
          />
        ) : namespaces.length === 0 ? (
          <div className="empty-state">No namespaces are registered yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th>Retention (days)</th>
                <th>Status</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {namespaces.map((ns: Namespace) => (
                <tr key={ns.namespaceId}>
                  <td>{ns.namespaceName}</td>
                  <td>{ns.description ?? "—"}</td>
                  <td>{ns.retentionDays}</td>
                  <td>{ns.status}</td>
                  <td>{new Date(ns.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}

export default Namespaces;
