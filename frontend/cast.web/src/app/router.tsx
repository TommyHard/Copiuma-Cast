import { Routes, Route, Navigate, useSearchParams } from 'react-router-dom';
import { ProtectedRoute } from '@/components/ProtectedRoute';
import { AppLayout } from '@/components/AppLayout';
import { LandingPage } from '@/features/landing/LandingPage';
import { AuthForm } from '@/features/auth/AuthForm';
import { DashboardPage } from '@/features/dashboard/DashboardPage';
import { GamesPage } from '@/features/games/GamesPage';
import { GameDetailPage } from '@/features/games/GameDetailPage';
import { ProfilePage } from '@/features/profile/ProfilePage';
import { MediaPage } from '@/features/media/MediaPage';
import { RoomPage } from '@/features/rooms/RoomPage';
import { SocialPage } from '@/features/social/SocialPage';
import { AdminPage } from '@/features/admin/AdminPage';
import { StreamerPage } from '@/features/streamer/StreamerPage';
import { NewsDetailPage } from '@/features/news/NewsDetailPage';
import { useAuthStore } from '@/store/auth';

function DesktopAutoRedirect() {
    const token = useAuthStore((s) => s.token);
    const [searchParams] = useSearchParams();
    const desktopCallback = searchParams.get('desktop');

    if (token && desktopCallback) {
        const url = new URL(desktopCallback);
        url.searchParams.set('token', token);
        window.location.href = url.toString();
        return <p className="p-8 text-fg-muted">Перенаправление в приложение...</p>;
    }

    return <Navigate to="/" replace />;
}

export function AppRoutes() {
    const authed = useAuthStore((s) => !!s.token);
    return (
        <Routes>
            <Route path="/welcome" element={<LandingPage />} />
            <Route path="/login" element={authed ? <DesktopAutoRedirect /> : <AuthForm mode="login" />} />
            <Route path="/register" element={authed ? <Navigate to="/" replace /> : <AuthForm mode="register" />} />

            <Route element={<ProtectedRoute />}>
                <Route element={<AppLayout />}>
                    <Route index element={<DashboardPage />} />
                    <Route path="news/:id" element={<NewsDetailPage />} />
                    <Route path="games" element={<GamesPage />} />
                    <Route path="games/:slug" element={<GameDetailPage />} />
                    <Route path="media" element={<MediaPage />} />
                    <Route path="rooms" element={<RoomPage />} />
                    <Route path="friends" element={<SocialPage />} />
                    <Route path="profile" element={<ProfilePage />} />
                    <Route path="streamer" element={<StreamerPage />} />
                    <Route path="admin" element={<AdminPage />} />
                </Route>
            </Route>

            <Route path="*" element={<Navigate to={authed ? '/' : '/welcome'} replace />} />
        </Routes>
    );
}