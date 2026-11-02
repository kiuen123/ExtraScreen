package com.extrascreen.decoder

import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Build
import android.view.Surface
import java.nio.ByteBuffer

class MediaCodecDecoder(
    private val surface: Surface,
    private val onRequestKeyframe: () -> Unit
) {
    private var codec: MediaCodec? = null
    @Volatile
    private var isConfigured = false
    private var mimeType: String = "video/avc"

    fun configure(mime: String, width: Int, height: Int, csdData: ByteArray? = null) {
        release()
        mimeType = mime

        try {
            val format = MediaFormat.createVideoFormat(mime, width, height)
            if (csdData != null && csdData.isNotEmpty()) {
                format.setByteBuffer("csd-0", ByteBuffer.wrap(csdData))
            }

            // Low latency tuning for Android 11+
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }

            val decoder = MediaCodec.createDecoderByType(mime)
            decoder.configure(format, surface, null, 0)
            decoder.start()

            codec = decoder
            isConfigured = true
        } catch (e: Exception) {
            onRequestKeyframe()
        }
    }

    fun decodeFrame(data: ByteArray, timestampUs: Long) {
        val decoder = codec ?: return
        if (!isConfigured) return

        try {
            val inputIndex = decoder.dequeueInputBuffer(10_000)
            if (inputIndex >= 0) {
                val inputBuffer = decoder.getInputBuffer(inputIndex)
                if (inputBuffer != null) {
                    inputBuffer.clear()
                    inputBuffer.put(data)
                    decoder.queueInputBuffer(inputIndex, 0, data.size, timestampUs, 0)
                }
            }

            // Drain output
            val bufferInfo = MediaCodec.BufferInfo()
            var outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
            while (outputIndex >= 0) {
                // Render directly to surface
                decoder.releaseOutputBuffer(outputIndex, true)
                outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
            }
        } catch (e: Exception) {
            // Decoder error: ask PC for an IDR keyframe to recover
            onRequestKeyframe()
        }
    }

    fun release() {
        isConfigured = false
        try {
            codec?.stop()
            codec?.release()
        } catch (e: Exception) {
            // Ignore
        }
        codec = null
    }
}
