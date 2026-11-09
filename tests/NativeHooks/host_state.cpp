#include "HostState.hpp"
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <limits>
#include <stdexcept>

extern "C" { __declspec(dllexport) void* aog_test_world = nullptr; }

static void require(bool ok, const char* message) {
    if (!ok) throw std::runtime_error(message);
}
template<class T> static void put(std::vector<std::uint8_t>& memory, std::size_t offset, T value) {
    std::memcpy(memory.data() + offset, &value, sizeof(value));
}

int main(int argc, char** argv) {
    try {
        require(argc == 2, "Expected authored configuration path");
        std::ifstream file(argv[1], std::ios::binary);
        std::vector<std::uint8_t> config((std::istreambuf_iterator<char>(file)), {});
        aog::HostStateReader reader(config, GetModuleHandleW(nullptr));
        require(!reader.sample(), "Absent world produced a sample");
        std::vector<std::uint8_t> world(0x12000), player(0x1000), modules(0x200), physics(0x200), controller(0x100), context(0x200);
        auto* player_pointer = player.data();
        put(world, 0x10ef8, &player_pointer); put(player, 0x190, modules.data());
        put(modules, 0x68, physics.data()); put(player, 0x6d0, std::uint32_t(123));
        put(physics, 0x70, std::array<float, 3>{ 3.5f, -4.0f, 9.25f });
        put(physics, 0xa8, controller.data()); put(controller, 0x18, context.data());
        put(context, 0xd0, std::array<float, 3>{ 200.0f, -30.0f, 400.0f });
        aog_test_world = world.data();
        auto state = reader.sample();
        require(state && state->map == 123 && state->position == std::array<float, 3>{ 3.5f, -4.0f, 9.25f }
            && state->origin == std::array<float, 3>{ 200.0f, -30.0f, 400.0f }, "Player/physics snapshot differs");
        put(player, 0x6d0, std::uint32_t(-1)); require(!reader.sample(), "Invalid map accepted");
        put(player, 0x6d0, std::uint32_t(123));
        put(physics, 0x70, std::numeric_limits<float>::quiet_NaN()); require(!reader.sample(), "Nonfinite position accepted");
        put(physics, 0x70, 3.5f);
        put(context, 0xd0, std::numeric_limits<float>::infinity()); require(!reader.sample(), "Nonfinite origin accepted");
        put(context, 0xd0, 200.0f);
        player_pointer = nullptr; require(!reader.sample(), "Absent player accepted");
        player_pointer = player.data();
        put(modules, 0x68, std::uintptr_t(1)); require(!reader.sample(), "Unreadable pointer accepted");
        put(modules, 0x68, physics.data()); require(reader.sample().has_value(), "Recovered player not sampled");
        for (auto at : { 0, 8, 16, 20, 24, 56 }) {
            auto bad = config; bad[at] ^= 0xff;
            bool rejected = false;
            try { aog::HostStateReader invalid(bad, GetModuleHandleW(nullptr)); }
            catch (const std::runtime_error&) { rejected = true; }
            require(rejected, "Invalid runtime configuration accepted");
        }
        auto truncated = config; truncated.pop_back();
        bool rejected = false;
        try { aog::HostStateReader invalid(truncated, GetModuleHandleW(nullptr)); }
        catch (const std::runtime_error&) { rejected = true; }
        require(rejected, "Truncated configuration accepted");
        std::puts("PASS: native player/physics sampling, missing/invalid state and rejected configuration/version/executable");
    } catch (const std::exception& e) { std::fprintf(stderr, "%s\n", e.what()); return 1; }
}
