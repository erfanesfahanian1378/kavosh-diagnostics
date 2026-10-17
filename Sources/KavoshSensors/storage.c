// Physical drive enumeration and NVMe SMART health log.
//
// The NVMe SMART interface is a private IOKit CFPlugIn that macOS exposes on every
// NVMe block storage device marked "NVMe SMART Capable". smartmontools reads drives the
// same way, and it works without administrator rights.

#include "KavoshSensors.h"

#ifdef __APPLE__

#include <IOKit/IOKitLib.h>
#include <IOKit/IOCFPlugIn.h>
#include <IOKit/IOBSD.h>
#include <string.h>

#define kKVNVMeSMARTUserClientTypeID CFUUIDGetConstantUUIDWithBytes(NULL, \
    0xAA, 0x0F, 0xA6, 0xF9, 0xC2, 0xD6, 0x45, 0x7F, 0xB1, 0x0B, 0x59, 0xA1, 0x32, 0x53, 0x29, 0x2F)

#define kKVNVMeSMARTInterfaceID CFUUIDGetConstantUUIDWithBytes(NULL, \
    0xCC, 0xD1, 0xDB, 0x19, 0xFD, 0x9A, 0x4D, 0xAF, 0xBF, 0x95, 0x12, 0x45, 0x4B, 0x23, 0x0A, 0xB6)

typedef struct KVNVMeSMARTInterface {
    IUNKNOWN_C_GUTS;
    UInt16 version;
    UInt16 revision;
    IOReturn (*SMARTReadData)(void *interface, void *smartLog512);
    IOReturn (*GetIdentifyData)(void *interface, void *identify, unsigned int ns);
    IOReturn (*GetFieldCounters)(void *interface, char *counters);
    IOReturn (*ScheduleBGRefresh)(void *interface);
    IOReturn (*GetLogPage)(void *interface, void *data, unsigned int a, unsigned int b);
    IOReturn (*GetSystemCounters)(void *interface, char *data, unsigned int *size);
    IOReturn (*GetAlgorithmCounters)(void *interface, char *data, unsigned int *size);
} KVNVMeSMARTInterface;

static bool kv_has_property(io_registry_entry_t entry, CFStringRef key) {
    CFTypeRef value = IORegistryEntryCreateCFProperty(entry, key, kCFAllocatorDefault, 0);
    if (value) {
        CFRelease(value);
        return true;
    }
    return false;
}

int kv_nvme_read_smart(const char *bsdName, uint8_t *outLog512) {
    if (bsdName == NULL || outLog512 == NULL) return -1;
    CFMutableDictionaryRef match = IOBSDNameMatching(MACH_PORT_NULL, 0, bsdName);
    if (match == NULL) return -1;
    io_service_t entry = IOServiceGetMatchingService(MACH_PORT_NULL, match); // consumes `match`
    if (entry == 0) return -2;

    // Walk up from the IOMedia to the device that offers the SMART interface.
    bool found = false;
    for (int depth = 0; depth < 16; depth++) {
        if (kv_has_property(entry, CFSTR("NVMe SMART Capable"))) {
            found = true;
            break;
        }
        io_registry_entry_t parent = 0;
        kern_return_t kr = IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent);
        IOObjectRelease(entry);
        entry = 0;
        if (kr != KERN_SUCCESS || parent == 0) break;
        entry = parent;
    }
    if (!found) {
        if (entry) IOObjectRelease(entry);
        return -3;
    }

    IOCFPlugInInterface **plugin = NULL;
    SInt32 score = 0;
    kern_return_t kr = IOCreatePlugInInterfaceForService(entry, kKVNVMeSMARTUserClientTypeID,
                                                         kIOCFPlugInInterfaceID, &plugin, &score);
    IOObjectRelease(entry);
    if (kr != KERN_SUCCESS || plugin == NULL) return -4;

    KVNVMeSMARTInterface **smart = NULL;
    HRESULT hr = (*plugin)->QueryInterface(plugin, CFUUIDGetUUIDBytes(kKVNVMeSMARTInterfaceID),
                                           (LPVOID *)&smart);
    if (hr != S_OK || smart == NULL) {
        IODestroyPlugInInterface(plugin);
        return -5;
    }

    memset(outLog512, 0, 512);
    IOReturn result = (*smart)->SMARTReadData(smart, outLog512);
    (*smart)->Release(smart);
    IODestroyPlugInInterface(plugin);
    return result == kIOReturnSuccess ? 0 : -6;
}

static void kv_copy_string(CFMutableDictionaryRef out, CFStringRef outKey, CFDictionaryRef from, CFStringRef fromKey) {
    if (from == NULL) return;
    CFTypeRef v = CFDictionaryGetValue(from, fromKey);
    if (v && CFGetTypeID(v) == CFStringGetTypeID()) {
        CFStringRef trimmed = CFStringCreateMutableCopy(kCFAllocatorDefault, 0, (CFStringRef)v);
        CFStringTrimWhitespace((CFMutableStringRef)trimmed);
        CFDictionarySetValue(out, outKey, trimmed);
        CFRelease(trimmed);
    }
}

static CFDictionaryRef kv_copy_dict_property(io_registry_entry_t entry, CFStringRef key) {
    CFTypeRef v = IORegistryEntryCreateCFProperty(entry, key, kCFAllocatorDefault, 0);
    if (v == NULL) return NULL;
    if (CFGetTypeID(v) != CFDictionaryGetTypeID()) {
        CFRelease(v);
        return NULL;
    }
    return (CFDictionaryRef)v;
}

static bool kv_capable_up_tree(io_registry_entry_t entry, CFStringRef key) {
    if (kv_has_property(entry, key)) return true;
    io_registry_entry_t current = entry;
    IOObjectRetain(current);
    bool result = false;
    for (int depth = 0; depth < 4; depth++) {
        io_registry_entry_t parent = 0;
        kern_return_t kr = IORegistryEntryGetParentEntry(current, kIOServicePlane, &parent);
        IOObjectRelease(current);
        current = 0;
        if (kr != KERN_SUCCESS || parent == 0) break;
        current = parent;
        if (kv_has_property(current, key)) {
            result = true;
            break;
        }
    }
    if (current) IOObjectRelease(current);
    return result;
}

int kv_copy_storage_devices(CFArrayRef *outArray) {
    if (outArray == NULL) return -1;
    *outArray = NULL;

    io_iterator_t iterator = 0;
    kern_return_t kr = IOServiceGetMatchingServices(MACH_PORT_NULL, IOServiceMatching("IOBlockStorageDevice"), &iterator);
    if (kr != KERN_SUCCESS) return -2;

    CFMutableArrayRef array = CFArrayCreateMutable(kCFAllocatorDefault, 0, &kCFTypeArrayCallBacks);
    io_object_t device;
    while ((device = IOIteratorNext(iterator)) != 0) {
        CFDictionaryRef deviceChars = kv_copy_dict_property(device, CFSTR("Device Characteristics"));
        CFDictionaryRef protocolChars = kv_copy_dict_property(device, CFSTR("Protocol Characteristics"));

        CFTypeRef bsd = IORegistryEntrySearchCFProperty(device, kIOServicePlane, CFSTR(kIOBSDNameKey),
                                                        kCFAllocatorDefault, kIORegistryIterateRecursively);
        CFTypeRef size = IORegistryEntrySearchCFProperty(device, kIOServicePlane, CFSTR("Size"),
                                                         kCFAllocatorDefault, kIORegistryIterateRecursively);
        CFTypeRef removable = IORegistryEntrySearchCFProperty(device, kIOServicePlane, CFSTR("Removable"),
                                                              kCFAllocatorDefault, kIORegistryIterateRecursively);

        if (deviceChars != NULL && bsd != NULL && CFGetTypeID(bsd) == CFStringGetTypeID()) {
            CFMutableDictionaryRef info = CFDictionaryCreateMutable(kCFAllocatorDefault, 0,
                                                                    &kCFTypeDictionaryKeyCallBacks,
                                                                    &kCFTypeDictionaryValueCallBacks);
            CFDictionarySetValue(info, CFSTR("bsdName"), bsd);
            kv_copy_string(info, CFSTR("model"), deviceChars, CFSTR("Product Name"));
            kv_copy_string(info, CFSTR("vendor"), deviceChars, CFSTR("Vendor Name"));
            kv_copy_string(info, CFSTR("medium"), deviceChars, CFSTR("Medium Type"));
            kv_copy_string(info, CFSTR("interconnect"), protocolChars, CFSTR("Physical Interconnect"));
            kv_copy_string(info, CFSTR("location"), protocolChars, CFSTR("Physical Interconnect Location"));
            if (size != NULL && CFGetTypeID(size) == CFNumberGetTypeID()) {
                CFDictionarySetValue(info, CFSTR("size"), size);
            }
            CFDictionarySetValue(info, CFSTR("nvmeSmart"),
                                 kv_capable_up_tree(device, CFSTR("NVMe SMART Capable")) ? kCFBooleanTrue : kCFBooleanFalse);
            CFDictionarySetValue(info, CFSTR("ataSmart"),
                                 kv_capable_up_tree(device, CFSTR("SMART Capable")) ? kCFBooleanTrue : kCFBooleanFalse);
            bool isRemovable = removable != NULL && CFGetTypeID(removable) == CFBooleanGetTypeID() &&
                               CFBooleanGetValue((CFBooleanRef)removable);
            CFDictionarySetValue(info, CFSTR("removable"), isRemovable ? kCFBooleanTrue : kCFBooleanFalse);
            CFArrayAppendValue(array, info);
            CFRelease(info);
        }

        if (deviceChars) CFRelease(deviceChars);
        if (protocolChars) CFRelease(protocolChars);
        if (bsd) CFRelease(bsd);
        if (size) CFRelease(size);
        if (removable) CFRelease(removable);
        IOObjectRelease(device);
    }
    IOObjectRelease(iterator);

    int count = (int)CFArrayGetCount(array);
    *outArray = array;
    return count;
}

#else // not Apple: stubs

int kv_nvme_read_smart(const char *bsdName, uint8_t *outLog512) { (void)bsdName; (void)outLog512; return -1; }

#endif
