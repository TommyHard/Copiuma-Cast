import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import svgr from 'vite-plugin-svgr';
import path from 'node:path';

const gateway = process.env.VITE_GATEWAY_URL || 'http://127.0.0.1:5208';

export default defineConfig({
    plugins: [react(), svgr()],
    resolve: {
        alias: { '@': path.resolve(__dirname, './src') },
    },
    server: {
        port: 3000,
        proxy: {
            '/api': { target: gateway, changeOrigin: true, secure: false },
            '/hubs': { target: gateway, changeOrigin: true, secure: false, ws: true },
            '/files': { target: gateway, changeOrigin: true, secure: false },
        },
    },
});