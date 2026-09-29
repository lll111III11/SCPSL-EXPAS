export PATH=/usr/bin:/mingw64/bin:$PATH
cd /d/ffbuild || exit 9
gcc -O2 -shared -o /d/ffbuild/EXPAS.AudioCodec.dll expas_codec.c \
    -I/d/ffbuild/out/include -L/d/ffbuild/out/lib \
    -lavformat -lavcodec -lavutil -lswresample \
    > /d/ffbuild/wrapper.log 2>&1
echo "GCC_EXIT=$?" >> /d/ffbuild/wrapper.log
