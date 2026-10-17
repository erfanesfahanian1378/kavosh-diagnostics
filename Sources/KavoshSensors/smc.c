// AppleSMC access. The SMC (System Management Controller) holds the Mac's temperature,
// fan and power sensors. Reading it needs no special rights.

#include "KavoshSensors.h"

#ifdef __APPLE__

#include <IOKit/IOKitLib.h>
#include <pthread.h>
#include <string.h>

#define KV_SMC_KERNEL_INDEX   2
#define KV_SMC_CMD_READ_BYTES 5
#define KV_SMC_CMD_READ_INDEX 8
#define KV_SMC_CMD_READ_INFO  9

typedef struct {
    char major;
    char minor;
    char build;
    char reserved[1];
    UInt16 release;
} kv_smc_vers_t;

typedef struct {
    UInt16 version;
    UInt16 length;
    UInt32 cpuPLimit;
    UInt32 gpuPLimit;
    UInt32 memPLimit;
} kv_smc_plimit_t;

typedef struct {
    UInt32 dataSize;
    UInt32 dataType;
    char dataAttributes;
} kv_smc_keyinfo_t;

typedef struct {
    UInt32 key;
    kv_smc_vers_t vers;
    kv_smc_plimit_t pLimitData;
    kv_smc_keyinfo_t keyInfo;
    char result;
    char status;
    char data8;
    UInt32 data32;
    unsigned char bytes[32];
} kv_smc_data_t;

_Static_assert(sizeof(kv_smc_data_t) == 80, "SMC parameter block must be 80 bytes");

static io_connect_t kv_conn = 0;
static pthread_mutex_t kv_lock = PTHREAD_MUTEX_INITIALIZER;

// Small cache so each key's type/size is only looked up once.
#define KV_INFO_CACHE 384
static struct { UInt32 key; UInt32 size; UInt32 type; } kv_info_cache[KV_INFO_CACHE];
static int kv_info_count = 0;

static UInt32 kv_fourcc(const char *s) {
    return ((UInt32)(unsigned char)s[0] << 24) | ((UInt32)(unsigned char)s[1] << 16) |
           ((UInt32)(unsigned char)s[2] << 8) | (UInt32)(unsigned char)s[3];
}

static void kv_fourcc_string(UInt32 v, char out[5]) {
    out[0] = (char)((v >> 24) & 0xff);
    out[1] = (char)((v >> 16) & 0xff);
    out[2] = (char)((v >> 8) & 0xff);
    out[3] = (char)(v & 0xff);
    out[4] = 0;
}

static kern_return_t kv_call(kv_smc_data_t *in, kv_smc_data_t *out) {
    size_t outSize = sizeof(kv_smc_data_t);
    return IOConnectCallStructMethod(kv_conn, KV_SMC_KERNEL_INDEX, in, sizeof(kv_smc_data_t), out, &outSize);
}

int kv_smc_open(void) {
    pthread_mutex_lock(&kv_lock);
    if (kv_conn != 0) {
        pthread_mutex_unlock(&kv_lock);
        return 0;
    }
    io_service_t service = IOServiceGetMatchingService(MACH_PORT_NULL, IOServiceMatching("AppleSMC"));
    if (service == 0) {
        pthread_mutex_unlock(&kv_lock);
        return -1;
    }
    io_connect_t conn = 0;
    kern_return_t kr = IOServiceOpen(service, mach_task_self(), 0, &conn);
    IOObjectRelease(service);
    if (kr != KERN_SUCCESS || conn == 0) {
        pthread_mutex_unlock(&kv_lock);
        return -2;
    }
    kv_conn = conn;
    pthread_mutex_unlock(&kv_lock);
    return 0;
}

void kv_smc_close(void) {
    pthread_mutex_lock(&kv_lock);
    if (kv_conn != 0) {
        IOServiceClose(kv_conn);
        kv_conn = 0;
    }
    kv_info_count = 0;
    pthread_mutex_unlock(&kv_lock);
}

bool kv_smc_is_open(void) {
    pthread_mutex_lock(&kv_lock);
    bool open = kv_conn != 0;
    pthread_mutex_unlock(&kv_lock);
    return open;
}

// Caller holds the lock.
static int kv_info_locked(UInt32 key, UInt32 *size, UInt32 *type) {
    for (int i = 0; i < kv_info_count; i++) {
        if (kv_info_cache[i].key == key) {
            *size = kv_info_cache[i].size;
            *type = kv_info_cache[i].type;
            return 0;
        }
    }
    kv_smc_data_t in, out;
    memset(&in, 0, sizeof(in));
    memset(&out, 0, sizeof(out));
    in.key = key;
    in.data8 = KV_SMC_CMD_READ_INFO;
    if (kv_call(&in, &out) != KERN_SUCCESS || out.result != 0) return -1;
    *size = out.keyInfo.dataSize;
    *type = out.keyInfo.dataType;
    if (kv_info_count < KV_INFO_CACHE) {
        kv_info_cache[kv_info_count].key = key;
        kv_info_cache[kv_info_count].size = *size;
        kv_info_cache[kv_info_count].type = *type;
        kv_info_count++;
    }
    return 0;
}

// Caller holds the lock.
static int kv_read_locked(UInt32 key, unsigned char bytes[32], UInt32 *size, UInt32 *type) {
    if (kv_conn == 0) return -1;
    if (kv_info_locked(key, size, type) != 0) return -2;
    if (*size == 0 || *size > 32) return -3;
    kv_smc_data_t in, out;
    memset(&in, 0, sizeof(in));
    memset(&out, 0, sizeof(out));
    in.key = key;
    in.keyInfo.dataSize = *size;
    in.data8 = KV_SMC_CMD_READ_BYTES;
    if (kv_call(&in, &out) != KERN_SUCCESS || out.result != 0) return -4;
    memcpy(bytes, out.bytes, 32);
    return 0;
}

static int kv_hex_digit(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

static int kv_decode(const char type[5], const unsigned char *b, UInt32 size, double *value) {
    if (strcmp(type, "flt ") == 0 && size == 4) {
        float f;
        memcpy(&f, b, 4); // little-endian on both Apple silicon and Intel
        *value = (double)f;
        return 0;
    }
    if ((type[0] == 'f' || type[0] == 's') && type[1] == 'p' && size == 2) {
        int frac = kv_hex_digit(type[3]);
        if (frac < 0) return -1;
        UInt16 raw = (UInt16)((b[0] << 8) | b[1]);
        double v = (type[0] == 's') ? (double)(int16_t)raw : (double)raw;
        *value = v / (double)(1 << frac);
        return 0;
    }
    if (strcmp(type, "ui8 ") == 0 || strcmp(type, "flag") == 0) {
        *value = (double)b[0];
        return 0;
    }
    if (strcmp(type, "si8 ") == 0) {
        *value = (double)(int8_t)b[0];
        return 0;
    }
    if (strcmp(type, "ui16") == 0 && size >= 2) {
        *value = (double)(UInt16)((b[0] << 8) | b[1]);
        return 0;
    }
    if (strcmp(type, "si16") == 0 && size >= 2) {
        *value = (double)(int16_t)((b[0] << 8) | b[1]);
        return 0;
    }
    if (strcmp(type, "ui32") == 0 && size >= 4) {
        *value = (double)(((UInt32)b[0] << 24) | ((UInt32)b[1] << 16) | ((UInt32)b[2] << 8) | (UInt32)b[3]);
        return 0;
    }
    if (strcmp(type, "si32") == 0 && size >= 4) {
        *value = (double)(int32_t)(((UInt32)b[0] << 24) | ((UInt32)b[1] << 16) | ((UInt32)b[2] << 8) | (UInt32)b[3]);
        return 0;
    }
    return -1;
}

int kv_smc_read_number(const char *key, double *value) {
    if (key == NULL || strlen(key) < 4 || value == NULL) return -1;
    pthread_mutex_lock(&kv_lock);
    unsigned char bytes[32];
    UInt32 size = 0, type = 0;
    int rc = kv_read_locked(kv_fourcc(key), bytes, &size, &type);
    pthread_mutex_unlock(&kv_lock);
    if (rc != 0) return rc;
    char typeStr[5];
    kv_fourcc_string(type, typeStr);
    return kv_decode(typeStr, bytes, size, value);
}

int kv_smc_key_info(const char *key, char type[5], uint32_t *size) {
    if (key == NULL || strlen(key) < 4) return -1;
    pthread_mutex_lock(&kv_lock);
    UInt32 s = 0, t = 0;
    int rc = kv_conn ? kv_info_locked(kv_fourcc(key), &s, &t) : -1;
    pthread_mutex_unlock(&kv_lock);
    if (rc != 0) return rc;
    if (type) kv_fourcc_string(t, type);
    if (size) *size = s;
    return 0;
}

uint32_t kv_smc_key_count(void) {
    double v = 0;
    if (kv_smc_read_number("#KEY", &v) != 0) return 0;
    if (v < 0 || v > 100000) return 0;
    return (uint32_t)v;
}

int kv_smc_key_at_index(uint32_t index, char key[5]) {
    pthread_mutex_lock(&kv_lock);
    if (kv_conn == 0) {
        pthread_mutex_unlock(&kv_lock);
        return -1;
    }
    kv_smc_data_t in, out;
    memset(&in, 0, sizeof(in));
    memset(&out, 0, sizeof(out));
    in.data8 = KV_SMC_CMD_READ_INDEX;
    in.data32 = index;
    kern_return_t kr = kv_call(&in, &out);
    pthread_mutex_unlock(&kv_lock);
    if (kr != KERN_SUCCESS || out.result != 0) return -2;
    kv_fourcc_string(out.key, key);
    return 0;
}

#else // not Apple: stubs

int kv_smc_open(void) { return -1; }
void kv_smc_close(void) {}
bool kv_smc_is_open(void) { return false; }
int kv_smc_read_number(const char *key, double *value) { (void)key; (void)value; return -1; }
int kv_smc_key_info(const char *key, char type[5], uint32_t *size) { (void)key; (void)type; (void)size; return -1; }
uint32_t kv_smc_key_count(void) { return 0; }
int kv_smc_key_at_index(uint32_t index, char key[5]) { (void)index; (void)key; return -1; }

#endif
