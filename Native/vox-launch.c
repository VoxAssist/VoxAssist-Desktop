#define _GNU_SOURCE
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <fcntl.h>
#include <string.h>
#include <errno.h>
#include <linux/uinput.h>

int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <path_to_dotnet_app> [args...]\n", argv[0]);
        return 1;
    }

    // 1. Open /dev/uinput
    int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
    if (fd < 0) {
        perror("Launcher: Failed to open /dev/uinput");
        return 1;
    }

    // 2. Initialize the virtual device
    ioctl(fd, UI_SET_EVBIT, EV_KEY);
    // Enable a wide range of keys (1 to 255)
    for (int i = 1; i < 255; i++) {
        ioctl(fd, UI_SET_KEYBIT, i);
    }

    struct uinput_setup usetup;
    memset(&usetup, 0, sizeof(usetup));
    usetup.id.bustype = BUS_USB;
    usetup.id.vendor = 0x1234;
    usetup.id.product = 0x5678;
    strcpy(usetup.name, "VoxAssist Virtual Keyboard");

    if (ioctl(fd, UI_DEV_SETUP, &usetup) < 0) {
        perror("Launcher: UI_DEV_SETUP failed");
        return 1;
    }

    if (ioctl(fd, UI_DEV_CREATE) < 0) {
        perror("Launcher: UI_DEV_CREATE failed");
        return 1;
    }

    // 3. Ensure the file descriptor is NOT closed on exec
    int flags = fcntl(fd, F_GETFD);
    fcntl(fd, F_SETFD, flags & ~FD_CLOEXEC);

    // 4. Pass the FD to the .NET app
    char fd_str[10];
    sprintf(fd_str, "%d", fd);
    setenv("VOXASSIST_UINPUT_FD", fd_str, 1);

    // 5. Execute
    if (execv(argv[1], &argv[1]) == -1) {
        perror("Launcher: execv failed");
        return 1;
    }

    return 0;
}
