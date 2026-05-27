import { Navigate, Outlet } from 'react-router-dom';
import { useAuthStore } from '@/store/auth';

export function ProtectedRoute() {
    const authed = useAuthStore((s) => !!s.token);
    return authed ? <Outlet /> : <Navigate to="/login" replace />;
}