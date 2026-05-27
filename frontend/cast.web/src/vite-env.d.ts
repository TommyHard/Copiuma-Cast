/// <reference types="vite/client" />
/// <reference types="vite-plugin-svgr/client" />

interface ImportMetaEnv {
    readonly VITE_GATEWAY_URL?: string;
    readonly VITE_IDENTITY_URL?: string;
    readonly VITE_DEFAULT_DESKTOP_CLIENT_ID?: string;
}

interface ImportMeta {
    readonly env: ImportMetaEnv;
}