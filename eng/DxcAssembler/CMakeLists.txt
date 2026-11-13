cmake_minimum_required(VERSION 3.20)
project(AgeOfGoldDxcAssembler LANGUAGES CXX)

set(AOG_DXC_ROOT "$ENV{AOG_DXC_ROOT}" CACHE PATH "External DXC distribution with headers and libraries")
find_path(DXC_INCLUDE_DIR dxcapi.h PATHS "${AOG_DXC_ROOT}/include/dxc" "${AOG_DXC_ROOT}/inc" NO_DEFAULT_PATH REQUIRED)
find_library(DXC_LIBRARY NAMES dxcompiler PATHS "${AOG_DXC_ROOT}/lib" "${AOG_DXC_ROOT}/lib/x64" NO_DEFAULT_PATH REQUIRED)
add_executable(aog-dxc-assemble main.cpp)
target_compile_features(aog-dxc-assemble PRIVATE cxx_std_17)
target_include_directories(aog-dxc-assemble PRIVATE "${DXC_INCLUDE_DIR}")
target_link_libraries(aog-dxc-assemble PRIVATE "${DXC_LIBRARY}")
if(WIN32 AND NOT MSVC)
  # The helper is copied beside DXC's libraries; it must not need the compiler's own runtime DLLs.
  target_link_options(aog-dxc-assemble PRIVATE -static)
endif()
