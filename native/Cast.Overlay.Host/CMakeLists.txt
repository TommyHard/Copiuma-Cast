# CMakeLists.txt — CEF-хост оверлея Copiuma.Cast.
#
# Этот таргет собирается ВНУТРИ дерева cef-project (или CEF binary distribution),
# где уже определены макросы CEF (SET_CEF_TARGET_OUT_DIR, COPY_FILES,
# SET_EXECUTABLE_TARGET_PROPERTIES и т.д.) и цели libcef_lib / libcef_dll_wrapper.
# Подключается из верхнего CMakeLists через add_subdirectory(...). См. инструкции
# в INTEGRATION.md рядом с этим файлом.
#
# Проект Windows-only (оверлей хукает Direct3D 9). Платформа сборки — x64:
# хост CEF — это ОТДЕЛЬНЫЙ процесс, он НЕ инжектится в игру, поэтому его
# разрядность не обязана совпадать с разрядностью игры. Shared memory
# работает межразрядно.

# --- Исходники --------------------------------------------------------------

set(CAST_OVERLAY_HOST_SRCS
  main.cpp
  OverlayApp.cpp
  OverlayApp.h
  OverlayClient.cpp
  OverlayClient.h
  SharedFrameWriter.cpp
  SharedFrameWriter.h
  SharedControlReader.cpp
  SharedControlReader.h
  # Контракты shared memory лежат в соседнем проекте Cast.Overlay (DLL-сторона).
  # Включаются относительным путём из OverlayClient/SharedFrameWriter/
  # SharedControlReader, поэтому каталог Cast.Overlay.Host обязан оставаться
  # соседом Cast.Overlay.
  ../Cast.Overlay/SharedFrame.h
  ../Cast.Overlay/SharedControl.h
)
source_group(Cast.Overlay.Host FILES ${CAST_OVERLAY_HOST_SRCS})

# --- Имя таргета ------------------------------------------------------------

set(CEF_TARGET "Cast.Overlay.Host")

# Каталог вывода (bin/<config>) определяет макрос cef-project.
SET_CEF_TARGET_OUT_DIR()

# --- Сборка (только Windows) ------------------------------------------------

if(OS_WINDOWS)
  # WIN32 → подсистема Windows (точка входа wWinMain, как в main.cpp).
  add_executable(${CEF_TARGET} WIN32 ${CAST_OVERLAY_HOST_SRCS})

  # Флаги компилятора/линкера, единые с остальными CEF-таргетами
  # (runtime library, /MT vs /MD, отключение лишних предупреждений и пр.).
  SET_EXECUTABLE_TARGET_PROPERTIES(${CEF_TARGET})

  # Заголовки CEF подключаются как "include/cef_app.h" — каталог CEF_ROOT
  # уже в путях через макросы cef-project. Добавляем путь к соседнему
  # Cast.Overlay для "../Cast.Overlay/SharedFrame.h" на всякий случай.
  target_include_directories(${CEF_TARGET} PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/.."
  )

  add_dependencies(${CEF_TARGET} libcef_dll_wrapper)

  target_link_libraries(${CEF_TARGET}
    "${CEF_ROOT}/Release/libcef.lib"
    libcef_dll_wrapper
    ${CEF_STANDARD_LIBS}
  )

  # main.cpp использует CefSettings.no_sandbox = true, поэтому cef_sandbox
  # не линкуем. Если соберёте cef-project с USE_SANDBOX=ON и захотите песочницу
  # — раскомментируйте блок ниже и уберите no_sandbox в main.cpp.
  # if(USE_SANDBOX)
  #   target_link_libraries(${CEF_TARGET} cef_sandbox_lib ${CEF_SANDBOX_STANDARD_LIBS})
  # endif()

  # Копируем рантайм CEF (libcef.dll, *.bin, *.pak, locales/ и т.д.) рядом с exe,
  # иначе хост не запустится.
  COPY_FILES("${CEF_TARGET}" "${CEF_BINARY_FILES}"   "${CEF_BINARY_DIR}"   "${CEF_TARGET_OUT_DIR}")
  COPY_FILES("${CEF_TARGET}" "${CEF_RESOURCE_FILES}" "${CEF_RESOURCE_DIR}" "${CEF_TARGET_OUT_DIR}")
else()
  message(FATAL_ERROR "Cast.Overlay.Host — только Windows.")
endif()
