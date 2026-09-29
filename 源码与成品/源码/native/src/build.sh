export PATH=/usr/bin:/mingw64/bin:$PATH
cd /d/ffbuild/ffmpeg-src || exit 9
make -j12 > /d/ffbuild/build.log 2>&1
echo "MAKE_EXIT=$?" >> /d/ffbuild/build.log
make install >> /d/ffbuild/build.log 2>&1
echo "INSTALL_EXIT=$?" >> /d/ffbuild/build.log
