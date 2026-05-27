// D3D9Renderer.h — рисует оверлей поверх кадра игры
//
// Принцип производительности: игра строит свой кадр обычно, а в самом
// конце (в хуке EndScene) добавляем одну операцию — отрисовку
// текстурированного прямоугольника с альфа-блендингом. Перед отрисовкой всё
// графическое состояние сохраняется в IDirect3DStateBlock9 и восстанавливается
// после, чтобы оверлей никак не влиял на рендеринг самой игры
//
// Источник изображения: если активен продюсер (CEF-хост или тестовый продюсер),
// его кадры берутся из shared memory и рисуются на весь экран. Если продюсера
// нет — показывается встроенный тест-паттерн
#pragma once

#include <d3d9.h>
#include <cstdint>
#include "SharedFrameReader.h"

namespace cast {

    class D3D9Renderer {
    public:
        bool Initialize(IDirect3DDevice9* device);
        bool IsInitialized() const { return m_initialized; }

        void Render(IDirect3DDevice9* device);

        void OnLostDevice();
        void OnResetDevice(IDirect3DDevice9* device);
        void Release();

    private:
        bool EnsureTexture(IDirect3DDevice9* device, UINT width, UINT height);
        void FillTestPattern();
        void UpdateFromShared();   // загрузить новый кадр из shared memory в текстуру

        IDirect3DDevice9* m_device = nullptr;
        IDirect3DTexture9* m_texture = nullptr;
        IDirect3DStateBlock9* m_stateBlock = nullptr;

        UINT m_texWidth = 0;
        UINT m_texHeight = 0;

        SharedFrameReader m_reader;
        LONG m_lastSeq = 0;
        bool m_haveProducerFrame = false;

        UINT m_panelWidth = 480;
        UINT m_panelHeight = 320;

        bool m_initialized = false;
    };
}