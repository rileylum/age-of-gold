#pragma once
#include <array>
#include <cstdint>
#include <optional>
#include <vector>
#include <windows.h>

namespace aog {
struct HostState {
    std::uint32_t map;
    std::array<float, 3> position;
    std::array<float, 3> origin;
};

class HostStateReader {
public:
    // Bind to the exact installed executable used to build this configuration.
    HostStateReader(const std::vector<std::uint8_t>& configuration, HMODULE host);
    // Missing or unloading player state produces no sample for this frame.
    std::optional<HostState> sample() const;
private:
    std::uintptr_t slot_ = 0;
    std::array<std::uint32_t, 8> fields_{};
};
}
