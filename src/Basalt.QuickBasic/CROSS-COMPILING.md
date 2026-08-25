# Building QuickBASIC programs for other systems

A QuickBASIC program is translated to C and handed to clang, so the IDE can
build for any target clang supports. What clang needs varies by target, and the
difference matters: it decides whether a build works out of the box or needs
setting up first.

## macOS, arm64 and x86-64 — works as installed

Apple's clang ships both architectures, so `-arch arm64` and `-arch x86_64` need
nothing further. Both are verified by the test suite, which builds a program for
the other architecture and reads back what `file` says about it.

## Linux — needs a sysroot

Clang can emit Linux code from macOS, but it has no Linux headers or libraries
to link against, so the failure appears at the link step rather than at
compilation, which makes it look like a compiler bug rather than a missing
dependency.

Provide a sysroot: a directory holding the target's `/usr/include` and
`/usr/lib`. The usual ways to obtain one:

- Copy them from the machine you are targeting.
- Extract them from a container image:

      docker create --name sysroot debian:bookworm
      docker export sysroot | tar -x -C ~/sysroots/linux-x64 usr lib
      docker rm sysroot

- Install a cross-compilation toolchain that brings its own; on Debian and
  Ubuntu, `gcc-aarch64-linux-gnu` puts one under
  `/usr/aarch64-linux-gnu`.

Then set the sysroot in the compilation request, or the `Sysroot` property of
the QuickBASIC project settings. Clang is invoked as:

    clang program.c -o program -target x86_64-unknown-linux-gnu --sysroot <path>

## Windows — needs a sysroot and the MSVC libraries

Same picture, with the added point that the Microsoft libraries are licensed:
they can be used to build for Windows, but not redistributed with this IDE.

Two routes work:

- **From Windows.** Install Visual Studio Build Tools; clang finds the
  libraries through the environment `vcvarsall.bat` sets up.
- **From macOS or Linux.** Use `xwin` to fetch the Windows SDK and CRT
  headers and libraries into a directory, then point the sysroot at it:

      xwin --accept-license splat --output ~/sysroots/win-x64

  and build with `-target x86_64-pc-windows-msvc --sysroot ~/sysroots/win-x64`.

The alternative is the MinGW toolchain, which needs no Microsoft libraries at
all; clang then targets `x86_64-w64-windows-gnu`. The produced binaries depend
on the MinGW runtime rather than the Microsoft one, which is a different
trade-off rather than a worse one.

## Why C rather than machine code

Going through C buys a real optimiser and every architecture clang supports,
for the cost of writing text. Emitting machine code directly would mean an
instruction selector and a register allocator for each architecture — the whole
of a compiler back end — to reach the same place.

It also means the generated code can be read. `KeepIntermediate` leaves the C
file beside the executable, which is the quickest way to see what a program
actually does.
