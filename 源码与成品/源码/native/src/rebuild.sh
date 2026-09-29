#!/bin/sh
# ============================================================================
#  EXPAS 原生音频解码器 —— 一键重建脚本 (路径自适应)
#   用法(在 MSYS2 的 MinGW64 shell 里):
#       sh rebuild.sh
#   需要: mingw-w64 gcc / nasm / make
#       pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm make
#
#   产物:
#       out/bin/*.dll  +  EXPAS.AudioCodec.dll
#   最后会自动把这几个 DLL 复制到上一级目录(native\), 之后运行 build.ps1 打包。
# ============================================================================
export PATH=/usr/bin:/mingw64/bin:$PATH
set -e

SRC=$(cd "$(dirname "$0")" && pwd)
FF="$SRC/ffmpeg-n9.0.1"
OUT="$SRC/out"
NATIVE="$SRC/.."

echo "== 1/3 configure ffmpeg (audio only) =="
cd "$FF"
./configure --prefix="$OUT" --target-os=mingw32 --arch=x86_64 \
  --enable-shared --disable-static --disable-programs --disable-doc --disable-debug --disable-network \
  --disable-avdevice --disable-swscale --disable-avfilter --disable-everything --disable-autodetect \
  --enable-protocol=file \
  --enable-demuxer=ogg,mov,matroska,mp3,flac,wav,asf,aac,aiff,ape,wv,ac3,eac3,dts,caf \
  --enable-decoder=vorbis,opus,mp3,mp3float,mp2,aac,aac_latm,alac,flac,wmav1,wmav2,wmapro,wmalossless,ac3,eac3,dca,truehd,mlp,ape,wavpack,mpc7,mpc8,speex,tta,nellymoser,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s24be,pcm_s32le,pcm_s32be,pcm_f32le,pcm_f32be,pcm_u8,pcm_s8,pcm_alaw,pcm_mulaw,adpcm_ima_wav,adpcm_ms,adpcm_ima_qt \
  --enable-parser=ogg,opus,vorbis,mpegaudio,aac,aac_latm,flac,ac3,dca \
  --enable-small

echo "== 2/3 make + install =="
make -j"$(nproc)"
make install

echo "== 3/3 build EXPAS.AudioCodec.dll =="
cd "$SRC"
gcc -O2 -shared -o "EXPAS.AudioCodec.dll" expas_codec.c \
    -I"$OUT/include" -L"$OUT/lib" -lavformat -lavcodec -lavutil -lswresample

echo "== copy into native\\ =="
for f in "$OUT"/bin/avcodec-*.dll "$OUT"/bin/avformat-*.dll "$OUT"/bin/avutil-*.dll "$OUT"/bin/swresample-*.dll; do
  [ -f "$f" ] && cp -f "$f" "$NATIVE/" && echo "   -> $(basename "$f")"
done
cp -f "EXPAS.AudioCodec.dll" "$NATIVE/" && echo "   -> EXPAS.AudioCodec.dll"
if [ ! -f "$NATIVE/libwinpthread-1.dll" ] && [ -f /mingw64/bin/libwinpthread-1.dll ]; then
  cp -f /mingw64/bin/libwinpthread-1.dll "$NATIVE/" && echo "   -> libwinpthread-1.dll"
fi

echo ""
echo "完成。现在运行:  powershell -ExecutionPolicy Bypass -File \"$SRC/build.ps1\""
echo "(该脚本会把 native\\*.dll 内嵌进 EXPAS.NP.dll 并输出到 源码与成品\\成品\\)"
