/* ============================================================================
 * EXPAS.AudioCodec —— 音频解码包装 DLL
 *   用 ffmpeg 共享库(avformat/avcodec/avutil/swresample)把任意音频/视频文件里的
 *   音轨解码为 **float32 / 单声道 / 48000Hz**(SCPSL SpeakerToy 所需口径)。
 *   只暴露 3 个极简 C API, 供 C# 直接 P/Invoke —— 无需在 C# 侧做 ffmpeg 复杂 interop。
 *
 * 编译(MSYS2 mingw64):
 *   gcc -O2 -shared -o EXPAS.AudioCodec.dll expas_codec.c \
 *       -lavformat -lavcodec -lavutil -lswresample
 * ========================================================================== */
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
#include <libavutil/channel_layout.h>
#include <libavutil/samplefmt.h>
#include <libavutil/mathematics.h>
#include <libswresample/swresample.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#define EXPAS_EXPORT __declspec(dllexport)

static void set_err(char *err, int len, const char *msg)
{
    if (err && len > 0) {
        strncpy(err, msg ? msg : "", (size_t)len - 1);
        err[len - 1] = 0;
    }
}

EXPAS_EXPORT const char *expas_codec_version(void)
{
    return "EXPAS.AudioCodec/1.0 (ffmpeg n9.0.1)";
}

EXPAS_EXPORT void expas_free(void *p)
{
    if (p) free(p);
}

/* 解码: 成功返回 0, *out_data = malloc 的 float 数组(用 expas_free 释放), *out_samples = 样本数 */
EXPAS_EXPORT int expas_decode(const char *path, int target_rate, int target_channels,
                              float **out_data, int *out_samples, char *err, int err_len)
{
    if (out_data) *out_data = NULL;
    if (out_samples) *out_samples = 0;
    if (!path) { set_err(err, err_len, "path is null"); return -1; }
    if (target_rate <= 0) target_rate = 48000;
    if (target_channels <= 0) target_channels = 1;

    /* 静音 ffmpeg 自己的日志(错误由返回值 + err 缓冲回传给调用方), 避免刷屏服务器控制台 */
    av_log_set_level(AV_LOG_QUIET);

    AVFormatContext *fmt = NULL;
    AVCodecContext *dec = NULL;
    SwrContext *swr = NULL;
    AVPacket *pkt = NULL;
    AVFrame *frame = NULL;
    AVChannelLayout out_layout;
    memset(&out_layout, 0, sizeof(out_layout));
    float *out = NULL;
    int out_cap = 0, out_len = 0, ret = -1, st;

    if (avformat_open_input(&fmt, path, NULL, NULL) < 0)
    { set_err(err, err_len, "open_input failed"); goto done; }
    if (avformat_find_stream_info(fmt, NULL) < 0)
    { set_err(err, err_len, "find_stream_info failed"); goto done; }

    st = av_find_best_stream(fmt, AVMEDIA_TYPE_AUDIO, -1, -1, NULL, 0);
    if (st < 0) { set_err(err, err_len, "no audio stream"); goto done; }

    const AVCodec *codec = avcodec_find_decoder(fmt->streams[st]->codecpar->codec_id);
    if (!codec) { set_err(err, err_len, "no decoder for codec"); goto done; }

    dec = avcodec_alloc_context3(codec);
    if (!dec) { set_err(err, err_len, "alloc codec ctx failed"); goto done; }
    if (avcodec_parameters_to_context(dec, fmt->streams[st]->codecpar) < 0)
    { set_err(err, err_len, "params_to_context failed"); goto done; }
    if (avcodec_open2(dec, codec, NULL) < 0)
    { set_err(err, err_len, "codec open failed"); goto done; }

    av_channel_layout_default(&out_layout, target_channels);
    if (swr_alloc_set_opts2(&swr, &out_layout, AV_SAMPLE_FMT_FLT, target_rate,
                            &dec->ch_layout, dec->sample_fmt, dec->sample_rate,
                            0, NULL) < 0 || !swr)
    { set_err(err, err_len, "swr alloc failed"); goto done; }
    if (swr_init(swr) < 0) { set_err(err, err_len, "swr init failed"); goto done; }

    pkt = av_packet_alloc();
    frame = av_frame_alloc();
    if (!pkt || !frame) { set_err(err, err_len, "alloc pkt/frame failed"); goto done; }

    for (;;) {
        int r = av_read_frame(fmt, pkt);
        if (r < 0) break;                       /* EOF 或错误 → 后面走 flush */
        if (pkt->stream_index == st) {
            if (avcodec_send_packet(dec, pkt) == 0) {
                while (avcodec_receive_frame(dec, frame) == 0) {
                    int max_out = (int)av_rescale_rnd(
                        swr_get_delay(swr, dec->sample_rate) + frame->nb_samples,
                        target_rate, dec->sample_rate, AV_ROUND_UP) + 256;
                    if (max_out <= 0) max_out = 1;
                    if (out_len + max_out > out_cap) {
                        int ncap = out_cap > 0 ? out_cap : 16384;
                        while (ncap < out_len + max_out) ncap *= 2;
                        float *np = (float *)realloc(out, (size_t)ncap * sizeof(float));
                        if (!np) { set_err(err, err_len, "out of memory"); goto done; }
                        out = np; out_cap = ncap;
                    }
                    uint8_t *op = (uint8_t *)(out + out_len);
                    int got = swr_convert(swr, &op, max_out,
                                          (const uint8_t **)frame->extended_data, frame->nb_samples);
                    if (got > 0) out_len += got;
                    av_frame_unref(frame);
                }
            }
        }
        av_packet_unref(pkt);
    }

    /* flush 解码器 */
    avcodec_send_packet(dec, NULL);
    while (avcodec_receive_frame(dec, frame) == 0) {
        int max_out = (int)av_rescale_rnd(
            swr_get_delay(swr, dec->sample_rate) + frame->nb_samples,
            target_rate, dec->sample_rate, AV_ROUND_UP) + 256;
        if (max_out <= 0) max_out = 1;
        if (out_len + max_out > out_cap) {
            int ncap = out_cap > 0 ? out_cap : 16384;
            while (ncap < out_len + max_out) ncap *= 2;
            float *np = (float *)realloc(out, (size_t)ncap * sizeof(float));
            if (!np) { set_err(err, err_len, "out of memory"); goto done; }
            out = np; out_cap = ncap;
        }
        uint8_t *op = (uint8_t *)(out + out_len);
        int got = swr_convert(swr, &op, max_out,
                              (const uint8_t **)frame->extended_data, frame->nb_samples);
        if (got > 0) out_len += got;
        av_frame_unref(frame);
    }

    if (out_len <= 0) { set_err(err, err_len, "decoded 0 samples"); goto done; }
    if (out_data) *out_data = out;
    if (out_samples) *out_samples = out_len;
    out = NULL;
    ret = 0;

done:
    if (out) free(out);
    if (frame) av_frame_free(&frame);
    if (pkt) av_packet_free(&pkt);
    if (swr) swr_free(&swr);
    if (dec) avcodec_free_context(&dec);
    if (fmt) avformat_close_input(&fmt);
    av_channel_layout_uninit(&out_layout);
    if (ret != 0 && err && err_len > 0 && err[0] == 0)
        set_err(err, err_len, "decode failed");
    return ret;
}
