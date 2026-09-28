The build puts three generated DLLs here and embeds them into the portable EXE:

- `ChaoGarage.dll`: validated virtual archive loader, source in `native/host`.
- `dinput8.dll`: loader bootstrap, source in `native/bootstrap`.
- `TextureRuntime.dll`: texture mod entry point, source in `native`.

Build with `native/build-loaders.cmd`, then `native/build.cmd`. The DLLs contain
no game assets. Generated DLLs are ignored by Git; GitHub Actions builds them
from the source in this repository.
