import { Outlet, NavLink, useLocation } from "react-router-dom";

const navItems: Array<{ to: string; label: string }> = [
  { to: "/", label: "Overview" },
  { to: "/workflows", label: "Workflows" },
  { to: "/task-queues", label: "Task Queues" },
  { to: "/namespaces", label: "Namespaces" },
  { to: "/workers", label: "Workers" }
];

function Layout() {
  const location = useLocation();

  return (
    <div className="app-shell">
      <header className="app-header">
        <h1>Odin Control Plane</h1>
        <nav className="app-nav" aria-label="Primary navigation">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) => (isActive || location.pathname.startsWith(item.to) && item.to !== "/" ? "active" : "")}
              end={item.to === "/"}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </header>
      <main className="app-main">
        <div className="app-content">
          <Outlet />
        </div>
      </main>
    </div>
  );
}

export default Layout;
