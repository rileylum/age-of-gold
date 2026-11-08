#include <cstdio>
#include <windows.h>
#include "MinHook.h"

static volatile int adjustment = 17;
__declspec(noinline) static int sample(int value) { return value + adjustment; }
static int (*original)(int);
static int replacement(int value) { return original(value) * 2; }

int main() {
    // Preserve a real call boundary so the check observes the installed hook.
    int (*volatile call)(int) = sample;
    if (call(3) != 20 || MH_Initialize() != MH_OK) return 1;
    if (MH_CreateHook((void *)sample, (void *)replacement, (void **)&original) != MH_OK) return 2;
    if (MH_EnableHook((void *)sample) != MH_OK || call(3) != 40) return 3;
    if (MH_DisableHook((void *)sample) != MH_OK || call(3) != 20) return 4;
    if (MH_RemoveHook((void *)sample) != MH_OK || MH_Uninitialize() != MH_OK) return 5;
    std::puts("PASS: authored function hook calls its original, disables and removes cleanly");
}
