import { Navigate, Route, Routes } from 'react-router';
import { useAuth } from './auth';
import Layout from './components/Layout';
import ForYou from './pages/ForYou';
import Login from './pages/Login';
import Profile from './pages/Profile';
import Saved from './pages/Saved';
import SinceLast from './pages/SinceLast';
import Taste from './pages/Taste';

export default function App() {
  const { user, loading } = useAuth();

  if (loading) return <div className="grid h-full place-items-center text-muted">Indlæser…</div>;
  if (!user) return <Login />;

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<SinceLast />} />
        <Route path="for-you" element={<ForYou />} />
        <Route path="taste" element={<Taste />} />
        <Route path="saved" element={<Saved />} />
        <Route path="profile" element={<Profile />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
