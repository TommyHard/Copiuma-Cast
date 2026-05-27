import {
  HubConnection,
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { useAuthStore } from '@/store/auth';

export function createHubConnection(path: string): HubConnection {
    return new HubConnectionBuilder()
        .withUrl(path, {
            accessTokenFactory: () => useAuthStore.getState().token ?? '',
            skipNegotiation: true,
            transport: HttpTransportType.WebSockets,
        })
        .withHubProtocol(new MessagePackHubProtocol())
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();
}

export const HUBS = {
    presence: '/hubs/presence',
    room: '/hubs/room',
} as const;