package com.extrascreen.audio

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import android.os.Build

/**
 * Bộ phát âm thanh PCM 48kHz 16-bit Stereo độ trễ thấp trên Android.
 * Trích xuất từ cấu trúc lớp H0 (SAAudioRenderer.a() / b()) của Spacedesk APK (qua REA).
 */
class AudioPlayer {
    private var audioTrack: AudioTrack? = null
    @Volatile
    private var isInitialized = false

    fun initialize(): Boolean {
        release()
        return try {
            val sampleRate = 48000
            val channelConfig = AudioFormat.CHANNEL_OUT_STEREO // 12
            val encoding = AudioFormat.ENCODING_PCM_16BIT // 2
            val minBufferSize = AudioTrack.getMinBufferSize(sampleRate, channelConfig, encoding)
                .coerceAtLeast(3840)

            val track = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                AudioTrack.Builder()
                    .setAudioAttributes(
                        AudioAttributes.Builder()
                            .setUsage(AudioAttributes.USAGE_MEDIA)
                            .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE)
                            .build()
                    )
                    .setAudioFormat(
                        AudioFormat.Builder()
                            .setEncoding(encoding)
                            .setSampleRate(sampleRate)
                            .setChannelMask(channelConfig)
                            .build()
                    )
                    .setBufferSizeInBytes(minBufferSize)
                    .setPerformanceMode(AudioTrack.PERFORMANCE_MODE_LOW_LATENCY)
                    .build()
            } else {
                @Suppress("DEPRECATION")
                AudioTrack(
                    AudioManager.STREAM_MUSIC,
                    sampleRate,
                    channelConfig,
                    encoding,
                    minBufferSize,
                    AudioTrack.MODE_STREAM
                )
            }

            track.play()
            audioTrack = track
            isInitialized = true
            true
        } catch (e: Exception) {
            isInitialized = false
            false
        }
    }

    fun writePcm(pcmData: ByteArray) {
        if (!isInitialized) {
            if (!initialize()) return
        }
        try {
            audioTrack?.write(pcmData, 0, pcmData.size)
        } catch (e: Exception) {
            // Ignore transient audio write errors
        }
    }

    fun release() {
        isInitialized = false
        try {
            audioTrack?.stop()
            audioTrack?.release()
        } catch (e: Exception) {
            // Ignore
        }
        audioTrack = null
    }
}
