#include "pch.h"
#include "D3D11Renderer.h"
#include "Logger.h"
#include <d3dcompiler.h>

namespace cast {

    // HLSL код шейдеров
    const char* g_shaderCode = R"(
        struct VS_OUT { float4 pos : SV_Position; float2 tex : TEXCOORD; };
        VS_OUT VS(uint id : SV_VertexID) {
            VS_OUT output;
            output.tex = float2((id << 1) & 2, id & 2);
            output.pos = float4(output.tex * float2(2, -2) + float2(-1, 1), 0, 1);
            return output;
        }
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);
        float4 PS(VS_OUT input) : SV_Target {
            return tex.Sample(samp, input.tex);
        }
    )";

    // Структура ручного сохранения состояния игры
    struct D3D11StateBackup {
        ID3D11DeviceContext* ctx;
        Microsoft::WRL::ComPtr<ID3D11BlendState> blendState;
        float blendFactor[4];
        UINT sampleMask;
        Microsoft::WRL::ComPtr<ID3D11DepthStencilState> depthStencilState;
        UINT stencilRef;
        Microsoft::WRL::ComPtr<ID3D11RasterizerState> rasterizerState;
        Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> psSRV;
        Microsoft::WRL::ComPtr<ID3D11SamplerState> psSampler;
        Microsoft::WRL::ComPtr<ID3D11PixelShader> ps;
        Microsoft::WRL::ComPtr<ID3D11VertexShader> vs;
        D3D11_PRIMITIVE_TOPOLOGY topology;

        Microsoft::WRL::ComPtr<ID3D11RenderTargetView> rtv;
        Microsoft::WRL::ComPtr<ID3D11DepthStencilView> dsv;
        UINT numViewports;
        D3D11_VIEWPORT viewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE];

        void Capture(ID3D11DeviceContext* context) {
            ctx = context;
            ctx->OMGetBlendState(&blendState, blendFactor, &sampleMask);
            ctx->OMGetDepthStencilState(&depthStencilState, &stencilRef);
            ctx->RSGetState(&rasterizerState);
            ctx->PSGetShaderResources(0, 1, &psSRV);
            ctx->PSGetSamplers(0, 1, &psSampler);
            ctx->PSGetShader(&ps, nullptr, nullptr);
            ctx->VSGetShader(&vs, nullptr, nullptr);
            ctx->IAGetPrimitiveTopology(&topology);

            ctx->OMGetRenderTargets(1, &rtv, &dsv);
            numViewports = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
            ctx->RSGetViewports(&numViewports, viewports);
        }

        void Restore() {
            ctx->OMSetBlendState(blendState.Get(), blendFactor, sampleMask);
            ctx->OMSetDepthStencilState(depthStencilState.Get(), stencilRef);
            ctx->RSSetState(rasterizerState.Get());
            ctx->PSSetShaderResources(0, 1, psSRV.GetAddressOf());
            ctx->PSSetSamplers(0, 1, psSampler.GetAddressOf());
            ctx->PSSetShader(ps.Get(), nullptr, 0);
            ctx->VSSetShader(vs.Get(), nullptr, 0);
            ctx->IASetPrimitiveTopology(topology);

            ctx->OMSetRenderTargets(1, rtv.GetAddressOf(), dsv.Get());
            if (numViewports > 0) ctx->RSSetViewports(numViewports, viewports);
        }
    };

    // Безопасная загрузка компилятора шейдеров в рантайме
    typedef HRESULT(WINAPI* pD3DCompile)(LPCVOID, SIZE_T, LPCSTR, const D3D_SHADER_MACRO*, ID3DInclude*, LPCSTR, LPCSTR, UINT, UINT, ID3DBlob**, ID3DBlob**);

    HRESULT SafeD3DCompile(const char* shaderCode, const char* entryPoint, const char* target, ID3DBlob** ppCode, ID3DBlob** ppError) {
        HMODULE hCompiler = LoadLibraryW(L"d3dcompiler_47.dll");
        if (!hCompiler) hCompiler = LoadLibraryW(L"d3dcompiler_46.dll");
        if (!hCompiler) return E_FAIL; // Если на ПК нет DirectX

        pD3DCompile compileFunc = (pD3DCompile)GetProcAddress(hCompiler, "D3DCompile");
        if (!compileFunc) return E_FAIL;

        return compileFunc(shaderCode, strlen(shaderCode), nullptr, nullptr, nullptr, entryPoint, target, 0, 0, ppCode, ppError);
    }

    bool D3D11Renderer::Initialize(ID3D11Device* device, ID3D11DeviceContext* context) {
        if (m_initialized) return true;

        m_device = device;
        m_context = context;
        m_reader.Open(GetCurrentProcessId());

        // Компиляция шейдеров
        Microsoft::WRL::ComPtr<ID3DBlob> vsBlob, psBlob, errorBlob;

        HRESULT hr = SafeD3DCompile(g_shaderCode, "VS", "vs_4_0", &vsBlob, &errorBlob);
        if (FAILED(hr)) { CAST_LOG_ERROR("VS Compile failed"); return false; }

        hr = SafeD3DCompile(g_shaderCode, "PS", "ps_4_0", &psBlob, &errorBlob);
        if (FAILED(hr)) { CAST_LOG_ERROR("PS Compile failed"); return false; }

        m_device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), nullptr, &m_vs);
        m_device->CreatePixelShader(psBlob->GetBufferPointer(), psBlob->GetBufferSize(), nullptr, &m_ps);

        // Стейты прозрачности и семплера
        D3D11_BLEND_DESC blendDesc = {};
        blendDesc.RenderTarget[0].BlendEnable = TRUE;
        // CEF OSR отдаёт premultiplied alpha -> SrcBlend = ONE (не SRC_ALPHA)
        blendDesc.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        blendDesc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        blendDesc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
        blendDesc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        blendDesc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        m_device->CreateBlendState(&blendDesc, &m_blendState);

        D3D11_SAMPLER_DESC sampDesc = {};
        sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        m_device->CreateSamplerState(&sampDesc, &m_samplerState);

        D3D11_DEPTH_STENCIL_DESC depthDesc = {};
        m_device->CreateDepthStencilState(&depthDesc, &m_depthState);

        m_initialized = true;
        CAST_LOG_INFO("D3D11Renderer initialized");
        return true;
    }

    bool D3D11Renderer::EnsureTexture(uint32_t width, uint32_t height) {
        if (m_texture && m_texWidth == width && m_texHeight == height) return true;

        m_texture.Reset();
        m_srv.Reset();

        D3D11_TEXTURE2D_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DYNAMIC;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

        HRESULT hr = m_device->CreateTexture2D(&desc, nullptr, &m_texture);
        if (FAILED(hr)) return false;

        m_device->CreateShaderResourceView(m_texture.Get(), nullptr, &m_srv);
        m_texWidth = width; m_texHeight = height;
        return true;
    }

    void D3D11Renderer::UpdateFromShared() {
        if (!m_reader.HasProducer()) return;

        const LONG seq = m_reader.Sequence();
        if (m_haveProducerFrame && seq == m_lastSeq) return;

        uint32_t w = 0, h = 0;
        if (!m_reader.PeekSize(w, h)) return;
        if (!EnsureTexture(w, h)) return;

        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(m_context->Map(m_texture.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            uint32_t gotW = 0, gotH = 0;
            bool ok = m_reader.ReadInto(static_cast<uint8_t*>(mapped.pData), mapped.RowPitch, gotW, gotH);
            m_context->Unmap(m_texture.Get(), 0);

            if (ok) {
                m_lastSeq = seq;
                m_haveProducerFrame = true;
            }
        }
    }

    void D3D11Renderer::Render(IDXGISwapChain* swapChain) {
        if (!m_initialized) return;

        UpdateFromShared();
        if (!m_haveProducerFrame) return;

        if (!m_mainRtv) {
            Microsoft::WRL::ComPtr<ID3D11Texture2D> backBuffer;
            if (SUCCEEDED(swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&backBuffer))) {
                m_device->CreateRenderTargetView(backBuffer.Get(), nullptr, &m_mainRtv);
            }
        }
        if (!m_mainRtv) return;

        D3D11StateBackup backup;
        backup.Capture(m_context.Get());

        // Указываем видеокарте, куда рисовать оверлей
        m_context->OMSetRenderTargets(1, m_mainRtv.GetAddressOf(), nullptr);

        // Настраиваем Viewport
        DXGI_SWAP_CHAIN_DESC sd;
        swapChain->GetDesc(&sd);
        D3D11_VIEWPORT vp = {};
        vp.Width = static_cast<float>(sd.BufferDesc.Width);
        vp.Height = static_cast<float>(sd.BufferDesc.Height);
        vp.MinDepth = 0.0f;
        vp.MaxDepth = 1.0f;
        m_context->RSSetViewports(1, &vp);

        m_context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        m_context->VSSetShader(m_vs.Get(), nullptr, 0);
        m_context->PSSetShader(m_ps.Get(), nullptr, 0);

        float blendFactor[4] = { 0.f, 0.f, 0.f, 0.f };
        m_context->OMSetBlendState(m_blendState.Get(), blendFactor, 0xffffffff);
        m_context->OMSetDepthStencilState(m_depthState.Get(), 0);

        m_context->PSSetShaderResources(0, 1, m_srv.GetAddressOf());
        m_context->PSSetSamplers(0, 1, m_samplerState.GetAddressOf());

        m_context->Draw(3, 0);

        backup.Restore();
    }

    void D3D11Renderer::Release() {
        m_reader.Close();
        m_texture.Reset(); m_srv.Reset(); m_vs.Reset(); m_ps.Reset();
        m_blendState.Reset(); m_samplerState.Reset(); m_depthState.Reset();
        m_mainRtv.Reset();
        m_device.Reset(); m_context.Reset();
        m_initialized = false;
    }
}