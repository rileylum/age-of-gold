#include "HostState.hpp"
#include <algorithm>
#include <bcrypt.h>
#include <cmath>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string>

namespace aog {
namespace {
template<class T> bool read(std::uintptr_t base, std::uint32_t offset, T& value) {
    if (!base || base > std::numeric_limits<std::uintptr_t>::max() - offset - sizeof(T)) return false;
    SIZE_T count = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(base + offset), &value, sizeof(T), &count)
        && count == sizeof(T);
}

template<class T> T field(const std::vector<std::uint8_t>& data, std::size_t offset) {
    T value;
    std::memcpy(&value, data.data() + offset, sizeof(value));
    return value;
}

std::array<std::uint8_t, 32> fingerprint(HMODULE module) {
    std::wstring path(32768, L'\0');
    const auto length = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
    if (!length || length >= path.size()) throw std::runtime_error("Cannot locate host executable");
    path.resize(length);
    struct File {
        HANDLE value;
        ~File() { if (value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    } file{CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr)};
    if (file.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot fingerprint host executable");
    struct Provider {
        BCRYPT_ALG_HANDLE value = nullptr;
        ~Provider() { if (value) BCryptCloseAlgorithmProvider(value, 0); }
    } provider;
    if (BCryptOpenAlgorithmProvider(&provider.value, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        throw std::runtime_error("Cannot open host fingerprint algorithm");
    ULONG size = 0, returned = 0;
    if (BCryptGetProperty(provider.value, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&size), sizeof(size), &returned, 0) < 0)
        throw std::runtime_error("Cannot size host fingerprint state");
    std::vector<std::uint8_t> storage(size);
    struct Hash {
        BCRYPT_HASH_HANDLE value = nullptr;
        ~Hash() { if (value) BCryptDestroyHash(value); }
    } hash;
    if (BCryptCreateHash(provider.value, &hash.value, storage.data(), size, nullptr, 0, 0) < 0)
        throw std::runtime_error("Cannot create host fingerprint state");
    std::array<std::uint8_t, 65536> buffer{};
    for (;;) {
        DWORD count = 0;
        if (!ReadFile(file.value, buffer.data(), static_cast<DWORD>(buffer.size()), &count, nullptr))
            throw std::runtime_error("Cannot read host executable");
        if (!count) break;
        if (BCryptHashData(hash.value, buffer.data(), count, 0) < 0) throw std::runtime_error("Cannot hash host executable");
    }
    std::array<std::uint8_t, 32> result{};
    if (BCryptFinishHash(hash.value, result.data(), static_cast<ULONG>(result.size()), 0) < 0)
        throw std::runtime_error("Cannot finish host fingerprint");
    return result;
}
}

HostStateReader::HostStateReader(const std::vector<std::uint8_t>& configuration, HMODULE host) {
    if (configuration.size() != 88 || std::memcmp(configuration.data(), "AOGSTATE", 8)
        || field<std::array<std::uint16_t, 4>>(configuration, 8) != std::array<std::uint16_t, 4>{ 2, 7, 2, 0 })
        throw std::runtime_error("Unsupported host-state configuration or host version");
    const auto size = field<std::uint32_t>(configuration, 16);
    const auto slot = field<std::uint32_t>(configuration, 20);
    const auto expected = field<std::array<std::uint8_t, 32>>(configuration, 24);
    fields_ = field<std::array<std::uint32_t, 8>>(configuration, 56);
    for (std::size_t i = 0; i < fields_.size(); i++) {
        const auto alignment = i == 3 || i == 4 || i == 7 ? 4u : 8u;
        if (!fields_[i] || fields_[i] > 0x20000 || fields_[i] % alignment)
            throw std::runtime_error("Invalid host-state field layout");
    }
    if (!host) throw std::runtime_error("Missing host module");
    const auto base = reinterpret_cast<std::uintptr_t>(host);
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS64 pe{};
    if (!read(base, 0, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < sizeof(dos) || dos.e_lfanew > 1024 * 1024
        || !read(base, static_cast<std::uint32_t>(dos.e_lfanew), pe) || pe.Signature != IMAGE_NT_SIGNATURE
        || pe.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 || pe.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
        || pe.FileHeader.SizeOfOptionalHeader != sizeof(IMAGE_OPTIONAL_HEADER64) || pe.OptionalHeader.SizeOfImage != size
        || !pe.FileHeader.NumberOfSections || pe.FileHeader.NumberOfSections > 96 || slot % 8 || slot > size || size - slot < 8)
        throw std::runtime_error("Host image does not match runtime configuration");
    bool writable = false;
    const auto sections = static_cast<std::uint32_t>(dos.e_lfanew) + sizeof(pe);
    for (unsigned i = 0; i < pe.FileHeader.NumberOfSections; i++) {
        IMAGE_SECTION_HEADER section{};
        if (!read(base, static_cast<std::uint32_t>(sections + i * sizeof(section)), section))
            throw std::runtime_error("Cannot read host sections");
        const auto length = std::max(section.Misc.VirtualSize, section.SizeOfRawData);
        if (slot >= section.VirtualAddress && std::uint64_t(slot) + 8 <= std::uint64_t(section.VirtualAddress) + length
            && (section.Characteristics & (IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE | IMAGE_SCN_MEM_EXECUTE)) == (IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE))
            writable = true;
    }
    if (!writable) throw std::runtime_error("Host world slot is not writable data");
    if (fingerprint(host) != expected) throw std::runtime_error("Host executable fingerprint changed; rebuild before loading");
    slot_ = base + slot;
}

std::optional<HostState> HostStateReader::sample() const {
    std::uintptr_t world = 0, players = 0, player = 0, modules = 0, physics = 0, controller = 0, context = 0;
    HostState result{};
    if (!read(slot_, 0, world) || !read(world, fields_[0], players) || !read(players, 0, player)
        || !read(player, fields_[1], modules) || !read(modules, fields_[2], physics)
        || !read(player, fields_[3], result.map) || result.map == 0 || result.map == UINT32_MAX
        || !read(physics, fields_[4], result.position) || !read(physics, fields_[5], controller)
        || !read(controller, fields_[6], context) || !read(context, fields_[7], result.origin)) return std::nullopt;
    for (auto v : result.position) if (!std::isfinite(v)) return std::nullopt;
    for (auto v : result.origin) if (!std::isfinite(v)) return std::nullopt;
    std::uintptr_t current_world = 0, current_player = 0;
    if (!read(slot_, 0, current_world) || current_world != world || !read(players, 0, current_player) || current_player != player)
        return std::nullopt;
    return result;
}
}
