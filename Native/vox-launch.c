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

    // 1. Try to open /dev/uinput
    int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
    
    if (fd >= 0) {
        // 2. Initialize the virtual device only if open succeeded
        ioctl(fd, UI_SET_EVBIT, EV_KEY);
        for (int i = 1; i < 255; i++) {
            ioctl(fd, UI_SET_KEYBIT, i);
        }

        struct uinput_setup usetup;
        memset(&usetup, 0, sizeof(usetup));
        usetup.id.bustype = BUS_USB;
        usetup.id.vendor = 0x1234;
        usetup.id.product = 0x5678;
        strcpy(usetup.name, "VoxAssist Virtual Keyboard");

        if (ioctl(fd, UI_DEV_SETUP, &usetup) >= 0 && ioctl(fd, UI_DEV_CREATE) >= 0) {
            // 3. Success! Pass the FD to the .NET app
            int flags = fcntl(fd, F_GETFD);
            fcntl(fd, F_SETFD, flags & ~FD_CLOEXEC);

            char fd_str[12];
            sprintf(fd_str, "%d", fd);
            setenv("VOXASSIST_UINPUT_FD", fd_str, 1);
        } else {
            fprintf(stderr, "Launcher: Device initialization failed. Continuing to app...\n");
            close(fd);
        }
    } else {
        fprintf(stderr, "Launcher: Permission denied for /dev/uinput. Continuing to app for fix...\n");
    }

    // 4. Always attempt to execute the main app
    // This allows the app to show its own "Fix Permissions" UI
    if (execv(argv[1], &argv[1]) == -1) {
        perror("Launcher: execv failed");
        return 1;
    }

    return 0;
}
