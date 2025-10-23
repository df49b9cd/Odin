import { Navigate, Route, Routes } from "react-router-dom";
import Layout from "./components/Layout";
import Dashboard from "./routes/Dashboard";
import Namespaces from "./routes/Namespaces";
import TaskQueues from "./routes/TaskQueues";
import Workflows from "./routes/Workflows";
import WorkflowDetail from "./routes/WorkflowDetail";
import Workers from "./routes/Workers";
import NotFound from "./routes/NotFound";
import "./App.css";

function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="workflows" element={<Workflows />} />
        <Route path="workflows/:workflowId" element={<WorkflowDetail />} />
        <Route path="task-queues" element={<TaskQueues />} />
        <Route path="namespaces" element={<Namespaces />} />
        <Route path="workers" element={<Workers />} />
        <Route path="not-found" element={<NotFound />} />
        <Route path="*" element={<Navigate to="/not-found" replace />} />
      </Route>
    </Routes>
  );
}

export default App;
