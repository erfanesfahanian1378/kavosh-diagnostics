// KavoshSensors — low-level IOKit access that is easier and safer to do in C.
//
//   * AppleSMC: temperatures, fan speeds and system power.
//   * NVMe SMART: the drive's own health log (same interface smartmontools uses).
//   * Physical drive list from the IORegistry.
//
// Every function is safe to call on a machine where the hardware is missing; it just
// returns a negative status. On non-Apple platforms everything is a stub so the rest of
// the package (and its tests) can still be built.

#ifndef KAVOSH_SENSORS_H
#define KAVOSH_SENSORS_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __APPLE__
#include <CoreFoundation/CoreFoundation.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

// MARK: - SMC

/// Opens the AppleSMC user client. Returns 0 on success.
int kv_smc_open(void);

/// Closes the connection (safe to call when not open).
void kv_smc_close(void);

/// True when the connection is open.
bool kv_smc_is_open(void);

/// Reads a 4-character key and decodes it into a number (handles flt, fpXY, spXY,
/// ui8/16/32 and si8/16/32). Returns 0 on success.
int kv_smc_read_number(const char *key, double *value);

/// Reads the data type of a key into `type` (4 chars + NUL) and its size. Returns 0 on success.
int kv_smc_key_info(const char *key, char type[5], uint32_t *size);

/// Number of keys the SMC exposes (0 when unavailable).
uint32_t kv_smc_key_count(void);

/// Writes the key at `index` into `key` (4 chars + NUL). Returns 0 on success.
int kv_smc_key_at_index(uint32_t index, char key[5]);

// MARK: - NVMe SMART

/// Reads the 512-byte NVMe SMART / health information log of the drive behind the BSD
/// name (for example "disk0"). Returns 0 on success.
int kv_nvme_read_smart(const char *bsdName, uint8_t *outLog512);

// MARK: - Drives

#ifdef __APPLE__
/// Fills `outArray` with a +1 retained CFArray of CFDictionaries, one per physical block
/// storage device. Keys: bsdName, model, vendor, medium, interconnect, location,
/// size (bytes), nvmeSmart (bool), ataSmart (bool), removable (bool).
/// Returns the number of devices, or a negative value on failure.
int kv_copy_storage_devices(CFArrayRef *outArray);
#endif

#ifdef __cplusplus
}
#endif

#endif // KAVOSH_SENSORS_H
