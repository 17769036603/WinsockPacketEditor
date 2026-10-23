#include <errno.h>
#include <stdint.h>
#include <stdlib.h>

void *reallocarray(void *ptr, size_t count, size_t size) {
    if (size != 0 && count > SIZE_MAX / size) {
        errno = ENOMEM;
        return NULL;
    }
    return realloc(ptr, count * size);
}
