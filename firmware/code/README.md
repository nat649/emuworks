# Embedded EmuWorks Code

Sources: [nat649/emuworks-code](https://github.com/nat649/emuworks-code). This directory contains the two compiled images, their exact revision and hashes in `source.json`, and their license notices. The application embeds the images and notices.

To update, build and test the firmware in its separate repository, commit the resulting images and sources, then run `node tools/update-code.cjs ../emuworks-code` from the emulator repository and rebuild the application. Commit the imported files here. No ARM compiler is required to build the Windows app using these precompiled images.
