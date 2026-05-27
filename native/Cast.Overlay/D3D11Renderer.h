#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include "SharedFrameReader.h"

namespace cast {

    class D3D11Renderer {
    public:
        bool Initialize(ID3D11Device* device, ID3D11DeviceContext* context);
        void Render(IDXGISwapChain* swapChain);
        void Release();

        bool IsInitialized() const { return m_initialized; }
        ID3D11Device* GetDevice() const { return m_device.Get(); }

    private:
        bool EnsureTexture(uint32_t width, uint32_t height);
        void UpdateFromShared();

        Microsoft::WRL::ComPtr<ID3D11Device> m_device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> m_context;
        Microsoft::WRL::ComPtr<ID3D11RenderTargetView> m_mainRtv;

        // Ресурсы
        Microsoft::WRL::ComPtr<ID3D11Texture2D> m_texture;
        Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> m_srv;
        Microsoft::WRL::ComPtr<ID3D11VertexShader> m_vs;
        Microsoft::WRL::ComPtr<ID3D11PixelShader> m_ps;
        Microsoft::WRL::ComPtr<ID3D11BlendState> m_blendState;
        Microsoft::WRL::ComPtr<ID3D11SamplerState> m_samplerState;
        Microsoft::WRL::ComPtr<ID3D11DepthStencilState> m_depthState;

        SharedFrameReader m_reader;
        LONG m_lastSeq = 0;
        bool m_haveProducerFrame = false;

        UINT m_texWidth = 0;
        UINT m_texHeight = 0;
        bool m_initialized = false;
    };
}