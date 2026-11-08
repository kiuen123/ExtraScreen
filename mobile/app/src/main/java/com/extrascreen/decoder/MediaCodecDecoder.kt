package com.extrascreen.decoder

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Build
import android.os.Process
import android.view.Surface
import java.nio.ByteBuffer
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Bộ giải mã phần cứng MediaCodec độ trễ cực thấp.
 * Đã tích hợp kỹ thuật từ y2.i(), O2.l(), O2.m() và R2.a() của Spacedesk APK (qua REA):
 * - Tách riêng csd-0 (SPS) và csd-1 (PPS) cho H.264
 * - Kích hoạt FEATURE_LowLatency + Vendor Low-Latency Flags (Kirin/Hisilicon, Qualcomm QTI, Exynos/MediaTek)
 * - Tách luồng giải mã riêng với THREAD_PRIORITY_DISPLAY để không chặn luồng đọc USB socket
 */
class MediaCodecDecoder(
    private val surface: Surface,
    private val onRequestKeyframe: () -> Unit,
    private val onFrameDecoded: ((decodeTimeMs: Double) -> Unit)? = null
) {
    private data class EncodedFrame(val data: ByteArray, val timestampUs: Long)

    private var codec: MediaCodec? = null
    private val isConfigured = AtomicBoolean(false)
    private val isRunning = AtomicBoolean(false)
    private var mimeType: String = "video/avc"
    private var currentWidth: Int = 1920
    private var currentHeight: Int = 1080
    private var cachedSps: ByteArray? = null
    private var cachedPps: ByteArray? = null

    private val frameQueue = LinkedBlockingQueue<EncodedFrame>(8)
    private var decodeThread: Thread? = null

    fun configure(mime: String, width: Int, height: Int, csdData: ByteArray? = null) {
        release()
        mimeType = mime
        currentWidth = width
        currentHeight = height

        try {
            val format = MediaFormat.createVideoFormat(mime, width, height)

            if (csdData != null && csdData.isNotEmpty()) {
                if (mime.equals("video/avc", ignoreCase = true)) {
                    // Tách riêng SPS (csd-0) và PPS (csd-1) theo R2.a() & y2.i() của Spacedesk
                    val csd = NalUnitParser.extractH264Csd(csdData)
                    if (csd.sps != null && csd.pps != null) {
                        cachedSps = csd.sps
                        cachedPps = csd.pps
                        format.setByteBuffer("csd-0", ByteBuffer.wrap(csd.sps))
                        format.setByteBuffer("csd-1", ByteBuffer.wrap(csd.pps))
                    } else {
                        format.setByteBuffer("csd-0", ByteBuffer.wrap(csdData))
                    }
                } else {
                    // H.265 / HEVC dùng csd-0 gộp VPS+SPS+PPS
                    format.setByteBuffer("csd-0", ByteBuffer.wrap(csdData))
                }
            }

            // Ưu tiên thời gian thực và tốc độ khung hình tối đa
            format.setInteger(MediaFormat.KEY_PRIORITY, 0)
            format.setInteger(MediaFormat.KEY_OPERATING_RATE, Short.MAX_VALUE.toInt())

            val decoder = MediaCodec.createDecoderByType(mime)
            val codecName = try {
                decoder.codecInfo.name.lowercase()
            } catch (e: Exception) {
                ""
            }

            // Chuẩn Android 11+ (SDK >= 30): kiểm tra isFeatureSupported("low-latency") giống y2.i()
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                try {
                    val caps = decoder.codecInfo.getCapabilitiesForType(mime)
                    if (caps.isFeatureSupported(MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency)) {
                        format.setFeatureEnabled(MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency, true)
                    }
                } catch (e: Exception) {
                    // Ignore capability query errors
                }
                format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }

            // Áp dụng các cờ Vendor Low-Latency (O2.l / O2.m của Spacedesk + Qualcomm/Samsung/MTK)
            applyVendorLowLatencyFlags(format, codecName)

            decoder.configure(format, surface, null, 0)
            decoder.start()

            codec = decoder
            isConfigured.set(true)
            startDecodeWorker()
        } catch (e: Exception) {
            onRequestKeyframe()
        }
    }

    private fun applyVendorLowLatencyFlags(format: MediaFormat, codecName: String) {
        // Từ O2.l() & O2.m() của Spacedesk APK: Kirin / Hisilicon decoder
        if (codecName.startsWith("c2.hisi") || codecName.startsWith("omx.hisi")) {
            format.setInteger("vendor.hisi-ext-low-latency-video-dec.video-scene-for-low-latency-req", 1)
        }
        // Các cờ vendor low-latency phổ biến trên Snapdragon (QTI), Exynos & MediaTek
        format.setInteger("vendor.qti-ext-dec-low-latency.enable", 1)
        format.setInteger("vendor.qti-ext-dec-picture-order.enable", 1)
        format.setInteger("vendor.rtc-ext-dec-low-latency.enable", 1)
        format.setInteger("vendor.hisi-ext-low-latency-video-dec.video-scene-for-low-latency-req", 1)
    }

    fun decodeFrame(data: ByteArray, timestampUs: Long) {
        if (!isConfigured.get()) return

        // Nếu hàng đợi giải mã bị đầy (> 8 frames), bỏ frame cũ nhất để giữ độ trễ kính-tới-kính thấp
        while (!frameQueue.offer(EncodedFrame(data, timestampUs))) {
            frameQueue.poll()
        }
    }

    private fun startDecodeWorker() {
        isRunning.set(true)
        decodeThread = Thread({
            try {
                Process.setThreadPriority(Process.THREAD_PRIORITY_DISPLAY)
            } catch (e: Exception) {
                // Ignore
            }

            val bufferInfo = MediaCodec.BufferInfo()

            while (isRunning.get()) {
                try {
                    val frame = frameQueue.poll(15, TimeUnit.MILLISECONDS)
                    val decoder = codec
                    if (decoder == null || !isConfigured.get()) continue

                    if (frame != null) {
                        val startNs = System.nanoTime()

                        // Kiểm tra nếu gói H.264 đính kèm SPS/PPS mới (như y2.f() của Spacedesk)
                        if (mimeType.equals("video/avc", ignoreCase = true)) {
                            val csd = NalUnitParser.extractH264Csd(frame.data)
                            if (csd.sps != null && csd.pps != null &&
                                (!csd.sps.contentEquals(cachedSps) || !csd.pps.contentEquals(cachedPps))
                            ) {
                                cachedSps = csd.sps
                                cachedPps = csd.pps
                            }
                        }

                        val inputIndex = decoder.dequeueInputBuffer(8_000)
                        if (inputIndex >= 0) {
                            val inputBuffer = decoder.getInputBuffer(inputIndex)
                            if (inputBuffer != null) {
                                inputBuffer.clear()
                                inputBuffer.put(frame.data)
                                decoder.queueInputBuffer(inputIndex, 0, frame.data.size, frame.timestampUs, 0)
                            }
                        }

                        var outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 4_000)
                        while (outputIndex >= 0) {
                            decoder.releaseOutputBuffer(outputIndex, true)
                            val elapsedMs = (System.nanoTime() - startNs) / 1_000_000.0
                            onFrameDecoded?.invoke(elapsedMs)
                            outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
                        }
                    } else {
                        // Vẫn drain output nếu có frame trễ trong pipeline phần cứng
                        var outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
                        while (outputIndex >= 0) {
                            decoder.releaseOutputBuffer(outputIndex, true)
                            outputIndex = decoder.dequeueOutputBuffer(bufferInfo, 0)
                        }
                    }
                } catch (e: InterruptedException) {
                    break
                } catch (e: Exception) {
                    if (isRunning.get()) {
                        onRequestKeyframe()
                    }
                }
            }
        }, "ExtraScreen-MediaCodecWorker").apply {
            isDaemon = true
            start()
        }
    }

    fun release() {
        isRunning.set(false)
        isConfigured.set(false)
        frameQueue.clear()
        try {
            decodeThread?.interrupt()
            decodeThread?.join(200)
        } catch (e: Exception) {
            // Ignore
        }
        decodeThread = null

        try {
            codec?.stop()
            codec?.release()
        } catch (e: Exception) {
            // Ignore
        }
        codec = null
    }
}
