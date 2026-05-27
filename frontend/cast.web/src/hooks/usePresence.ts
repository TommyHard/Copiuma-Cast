import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { HubConnectionState } from '@microsoft/signalr';
import { createHubConnection, HUBS } from '@/lib/signalr';
import { useAuthStore } from '@/store/auth';

export function usePresence() {
    const qc = useQueryClient();
    const token = useAuthStore((s) => s.token);

    useEffect(() => {
        if (!token) return;

        const conn = createHubConnection(HUBS.presence);
        let startPromise: Promise<void> | null = null;
        let isMounted = true;

        conn.on('PresenceChanged', () => {
            void qc.invalidateQueries({ queryKey: ['friends'] });
            void qc.invalidateQueries({ queryKey: ['following'] });
        });

        startPromise = conn.start().catch(() => {
            // ignore
        });

        return () => {
            isMounted = false;

            const stopConnection = () => {
                if (conn.state !== HubConnectionState.Disconnected) {
                    conn.stop().catch(() => { });
                }
            };

            if (conn.state === HubConnectionState.Connected) {
                stopConnection();
            }
            else if (startPromise) {
                startPromise.finally(() => {
                    if (!isMounted) stopConnection();
                });
            }
        };
    }, [token, qc]);
}