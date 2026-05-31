// Overlay.h — оркестратор оверлея
// Управляет жизненным циклом (инициализация/выгрузка) и глобальным
// состоянием видимости. Видимость читается из хука рендера
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

	// Виртуальный код клавиши открытия оверлея (по умолчанию F8). Читается из
	// %TEMP%\CopiumaCast\overlay-hotkey, который пишет Cast.Desktop (настройка
	// пользователя). Используется и опросом, и WndProc-хуком
	int ToggleKey();
	void LoadToggleKey();
}