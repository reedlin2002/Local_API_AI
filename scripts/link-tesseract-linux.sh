#!/usr/bin/env sh
# The Tesseract NuGet package only bundles Windows binaries. On Linux it looks for
#   <app dir>/x64/libleptonica-1.82.0.so and <app dir>/x64/libtesseract50.so
# so we point those names at the distro's libraries (Ubuntu 24.04: liblept5 1.82.0, libtesseract5 5.3.x).
# Requires: apt-get install libtesseract5. Run as root (it may add a libdl.so symlink).
set -eu

target="${1:?usage: link-tesseract-linux.sh <app-output-dir>}"

tesseract="$(find /usr/lib -name 'libtesseract.so.5' | head -n 1)"
leptonica="$(find /usr/lib -name 'liblept.so.5' | head -n 1)"
if [ -z "$tesseract" ] || [ -z "$leptonica" ]; then
  echo "libtesseract.so.5 / liblept.so.5 not found; install libtesseract5 first" >&2
  exit 1
fi

mkdir -p "$target/x64"
ln -sf "$tesseract" "$target/x64/libtesseract50.so"
ln -sf "$leptonica" "$target/x64/libleptonica-1.82.0.so"

# The wrapper dlopen()s "libdl.so"; glibc >= 2.34 only ships libdl.so.2.
libdir="$(dirname "$tesseract")"
if [ ! -e "$libdir/libdl.so" ] && [ -e "$libdir/libdl.so.2" ]; then
  ln -s "$libdir/libdl.so.2" "$libdir/libdl.so"
fi

echo "Linked Tesseract native libraries into $target/x64"
