export PATH=/usr/bin:/mingw64/bin:$PATH
cd /d/ffbuild/ffmpeg-src || exit 9
./configure \
  --prefix=/d/ffbuild/out \
  --target-os=mingw32 --arch=x86_64 \
  --enable-shared --disable-static \
  --disable-programs --disable-doc --disable-debug --disable-network \
  --disable-avdevice --disable-swscale --disable-avfilter \
  --disable-everything --disable-autodetect \
  --enable-protocol=file \
  --enable-demuxer=ogg,mov,matroska,mp3,flac,wav,asf,aac,aiff,ape,wv,ac3,eac3,dts,caf \
  --enable-decoder=vorbis,opus,mp3,mp3float,mp2,aac,aac_latm,alac,flac,wmav1,wmav2,wmapro,wmalossless,ac3,eac3,dca,truehd,mlp,ape,wavpack,mpc7,mpc8,speex,tta,nellymoser,pcm_s16le,pcm_s16be,pcm_s24le,pcm_s24be,pcm_s32le,pcm_s32be,pcm_f32le,pcm_f32be,pcm_u8,pcm_s8,pcm_alaw,pcm_mulaw,adpcm_ima_wav,adpcm_ms,adpcm_ima_qt \
  --enable-parser=ogg,opus,vorbis,mpegaudio,aac,aac_latm,flac,ac3,dca \
  --enable-small \
  > /d/ffbuild/configure.log 2>&1
echo "CONFIGURE_EXIT=$?" >> /d/ffbuild/configure.log
