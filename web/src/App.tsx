import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginPage, RegisterPage, RequireAdmin, RequireAuth } from './auth/AuthPages';
import { Layout } from './components/Layout';
import { AdminUsersPage } from './pages/AdminUsersPage';
import { MyTasksPage } from './pages/MyTasksPage';
import { ProjectPage } from './pages/ProjectPage';
import { ProjectsPage } from './pages/ProjectsPage';

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route element={<RequireAuth><Layout /></RequireAuth>}>
        <Route path="/projects" element={<ProjectsPage />} />
        <Route path="/projects/:projectId" element={<ProjectPage />} />
        <Route path="/my-tasks" element={<MyTasksPage />} />
        <Route path="/admin/users" element={<RequireAdmin><AdminUsersPage /></RequireAdmin>} />
      </Route>
      <Route path="*" element={<Navigate to="/projects" replace />} />
    </Routes>
  );
}
