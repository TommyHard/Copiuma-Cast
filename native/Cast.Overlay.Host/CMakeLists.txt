# CMakeLists.txt — CEF-хост оверлея Copiuma.Cast
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
  ../Cast.Overlay/SharedFrame.h
  ../Cast.Overlay/SharedControl.h
)
source_group(Cast.Overlay.Host FILES ${CAST_OVERLAY_HOST_SRCS})

# --- Имя таргета ------------------------------------------------------------

set(CEF_TARGET "Cast.Overlay.Host")

# Каталог вывода (bin/<config>) определяет макрос cef-project
SET_CEF_TARGET_OUT_DIR()

# --- Сборка (только Windows) ------------------------------------------------

if(OS_WINDOWS)
  add_executable(${CEF_TARGET} WIN32 ${CAST_OVERLAY_HOST_SRCS})

  SET_EXECUTABLE_TARGET_PROPERTIES(${CEF_TARGET})

  target_include_directories(${CEF_TARGET} PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/.."
  )

  add_dependencies(${CEF_TARGET} libcef_dll_wrapper)

  target_link_libraries(${CEF_TARGET}
    "${CEF_ROOT}/Release/libcef.lib"
    libcef_dll_wrapper
    ${CEF_STANDARD_LIBS}
  )

  COPY_FILES("${CEF_TARGET}" "${CEF_BINARY_FILES}"   "${CEF_BINARY_DIR}"   "${CEF_TARGET_OUT_DIR}")
  COPY_FILES("${CEF_TARGET}" "${CEF_RESOURCE_FILES}" "${CEF_RESOURCE_DIR}" "${CEF_TARGET_OUT_DIR}")
else()
  message(FATAL_ERROR "Cast.Overlay.Host — только Windows.")
endif()