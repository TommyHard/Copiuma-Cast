// Overlay.h — оркестратор оверлея
// Управляет жизненным циклом (инициализация/выгрузка) и глобальным
// состоянием видимости. Видимость читается из хука рендера, поэтому
// она вынесена в атомарный флаг
#pragma once

namespace cast::overlay {

	// Запускается из отдельного потока (НЕ из DllMain, чтобы не держать loader lock)
	void Initialize();

	// Снимает хуки и освобождает ресурсы
	void Shutdown();

	// Видим ли оверлей сейчас (читает хук EndScene каждый кадр)
	bool IsVisible();
	void SetVisible(bool visible);
	void ToggleVisible();

}