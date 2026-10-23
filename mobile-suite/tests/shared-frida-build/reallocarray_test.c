#include <assert.h>
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>

void *reallocarray(void *ptr, size_t count, size_t size);

int main(void) {
    errno = EDOM;
    void *zero = reallocarray(NULL, 0, sizeof(int));
    assert(errno == EDOM);
    free(zero);

    int *values = reallocarray(NULL, 2, sizeof(int));
    assert(values != NULL);
    assert(errno == EDOM);
    values[0] = 7;
    values[1] = 11;
    int *grown = reallocarray(values, 4, sizeof(int));
    assert(grown != NULL);
    assert(grown[0] == 7 && grown[1] == 11);
    free(grown);

    errno = 0;
    assert(reallocarray(NULL, SIZE_MAX, 2) == NULL);
    assert(errno == ENOMEM);
    return 0;
}
