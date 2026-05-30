#include "pch.h"
#include "D3D9Renderer.h"
#include "Logger.h"

#pragma comment(lib, "d3d9.lib")

namespace cast {
    namespace {

        struct OverlayVertex {
            float x, y, z, rhw;
            float u, v;
        };

        constexpr DWORD kOverlayFVF = D3DFVF_XYZRHW | D3DFVF_TEX1;

        inline uint32_t Argb(uint8_t a, uint8_t r, uint8_t g, uint8_t b)
        {
            return (uint32_t(a) << 24) | (uint32_t(r) << 16) | (uint32_t(g) << 8) | uint32_t(b);
        }

    }

    bool D3D9Renderer::Initialize(IDirect3DDevice9* device)
    {
        if (m_initialized)
            return true;

        m_device = device;

        // Открываем канал shared memory для текущего процесса игры
        m_reader.Open(GetCurrentProcessId());

        // Стартовая текстура — под тест-паттерн (пока продюсера нет)
        if (!EnsureTexture(device, m_panelWidth, m_panelHeight))
        {
            CAST_LOG_ERROR("D3D9Renderer: initial EnsureTexture failed");
            return false;
        }
        FillTestPattern();

        m_initialized = true;
        CAST_LOG_INFO("D3D9Renderer initialized");
        return true;
    }

    bool D3D9Renderer::EnsureTexture(IDirect3DDevice9* device, UINT width, UINT height)
    {
        if (m_texture && m_texWidth == width && m_texHeight == height)
            return true;

        if (m_texture) { m_texture->Release(); m_texture = nullptr; }

        HRESULT hr = device->CreateTexture(
            width, height, 1,
            D3DUSAGE_DYNAMIC, D3DFMT_A8R8G8B8, D3DPOOL_DEFAULT,
            &m_texture, nullptr);
        if (FAILED(hr))
        {
            CAST_LOG_ERROR("CreateTexture {}x{} failed: 0x{:08X}", width, height,
                static_cast<uint32_t>(hr));
            m_texWidth = m_texHeight = 0;
            return false;
        }

        m_texWidth = width;
        m_texHeight = height;
        return true;
    }

    void D3D9Renderer::FillTestPattern()
    {
        if (!m_texture)
            return;

        D3DLOCKED_RECT lr{};
        if (FAILED(m_texture->LockRect(0, &lr, nullptr, D3DLOCK_DISCARD)))
            return;

        auto* base = static_cast<uint8_t*>(lr.pBits);
        for (UINT y = 0; y < m_texHeight; ++y)
        {
            auto* row = reinterpret_cast<uint32_t*>(base + y * lr.Pitch);
            for (UINT x = 0; x < m_texWidth; ++x)
            {
                const bool border =
                    x < 3 || y < 3 || x >= m_texWidth - 3 || y >= m_texHeight - 3;

                if (border)
                {
                    row[x] = Argb(255, 0, 200, 255); // рамка (cyan)
                }
                else
                {
                    const uint8_t g = static_cast<uint8_t>((x + y) & 0xFF);
                    row[x] = Argb(180, static_cast<uint8_t>(20 + g / 8),
                        static_cast<uint8_t>(20 + g / 6),
                        static_cast<uint8_t>(28 + g / 4));
                }
            }
        }

        m_texture->UnlockRect(0);
        m_haveProducerFrame = false;
    }

    void D3D9Renderer::UpdateFromShared()
    {
        if (!m_reader.HasProducer())
        {
            // Продюсер ещё не появился (или отключился): показываем тест-паттерн
            if (m_haveProducerFrame)
            {
                EnsureTexture(m_device, m_panelWidth, m_panelHeight);
                FillTestPattern();
            }
            return;
        }

        const LONG seq = m_reader.Sequence();
        if (m_haveProducerFrame && seq == m_lastSeq)
            return; // новых кадров нет — текстура уже актуальна

        uint32_t w = 0, h = 0;
        if (!m_reader.PeekSize(w, h))
            return; // прямо сейчас идёт запись — попробуем на следующем кадре

        if (!EnsureTexture(m_device, w, h))
            return;

        D3DLOCKED_RECT lr{};
        if (FAILED(m_texture->LockRect(0, &lr, nullptr, D3DLOCK_DISCARD)))
            return;

        uint32_t gotW = 0, gotH = 0;
        const bool ok = m_reader.ReadInto(static_cast<uint8_t*>(lr.pBits),
            static_cast<uint32_t>(lr.Pitch), gotW, gotH);
        m_texture->UnlockRect(0);

        if (ok)
        {
            m_lastSeq = seq;
            m_haveProducerFrame = true;
        }
    }

    void D3D9Renderer::Render(IDirect3DDevice9* device)
    {
        if (!m_initialized || !m_texture)
            return;

        UpdateFromShared();

        // Сохраняем состояние устройства
        if (!m_stateBlock)
        {
            if (FAILED(device->CreateStateBlock(D3DSBT_ALL, &m_stateBlock)))
                return;
        }
        m_stateBlock->Capture();

        D3DVIEWPORT9 vp{};
        device->GetViewport(&vp);

        // Кадр продюсера — на весь экран; тест-паттерн — панель по центру
        float l, t, r, b;
        if (m_haveProducerFrame)
        {
            l = -0.5f;                      t = -0.5f;
            r = float(vp.Width) - 0.5f;     b = float(vp.Height) - 0.5f;
        }
        else
        {
            const float px = float((vp.Width - m_texWidth) / 2);
            const float py = float((vp.Height - m_texHeight) / 2);
            l = px - 0.5f;                  t = py - 0.5f;
            r = px + m_texWidth - 0.5f;     b = py + m_texHeight - 0.5f;
        }

        device->SetVertexShader(nullptr);
        device->SetPixelShader(nullptr);
        device->SetFVF(kOverlayFVF);

        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        // CEF OSR отдаёт premultiplied alpha, поэтому SRCBLEND = ONE (не SRCALPHA),
        // иначе на сглаженных краях текста и полупрозрачных панелях тёмные ореолы
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
        device->SetRenderState(D3DRS_ZENABLE, FALSE);
        device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
        device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
        device->SetRenderState(D3DRS_LIGHTING, FALSE);
        device->SetRenderState(D3DRS_FOGENABLE, FALSE);
        device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
        device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
        device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
        device->SetRenderState(D3DRS_SRGBWRITEENABLE, FALSE);

        device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_TEXTURE);
        device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_TEXTURE);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
        device->SetSamplerState(0, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);

        device->SetTexture(0, m_texture);

        const OverlayVertex quad[4] = {
            { l, t, 0.0f, 1.0f, 0.0f, 0.0f },
            { r, t, 0.0f, 1.0f, 1.0f, 0.0f },
            { l, b, 0.0f, 1.0f, 0.0f, 1.0f },
            { r, b, 0.0f, 1.0f, 1.0f, 1.0f },
        };
        device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(OverlayVertex));

        m_stateBlock->Apply();
    }

    void D3D9Renderer::OnLostDevice()
    {
        if (m_stateBlock) { m_stateBlock->Release(); m_stateBlock = nullptr; }
        if (m_texture) { m_texture->Release();    m_texture = nullptr; }
        m_texWidth = m_texHeight = 0;
        m_haveProducerFrame = false;
        m_lastSeq = 0;
        CAST_LOG_INFO("D3D9Renderer: resources released (device lost)");
    }

    void D3D9Renderer::OnResetDevice(IDirect3DDevice9* device)
    {
        m_device = device;
        EnsureTexture(device, m_panelWidth, m_panelHeight);
        FillTestPattern();
        CAST_LOG_INFO("D3D9Renderer: resources recreated (device reset)");
    }

    void D3D9Renderer::Release()
    {
        OnLostDevice();
        m_reader.Close();
        m_device = nullptr;
        m_initialized = false;
    }

}