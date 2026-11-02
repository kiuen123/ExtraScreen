package com.extrascreen.stats

import android.os.SystemClock
import com.extrascreen.protocol.Packet
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong

class StatsCollector(
    private val onSendPacket: (Packet) -> Unit
) {
    private val frameCount = AtomicInteger(0)
    private val byteCount = AtomicLong(0)
    private var lastCalculationTime = SystemClock.elapsedRealtime()

    var currentFps: Double = 60.0
        private set
    var currentLatencyMs: Double = 38.0
        private set
    var currentBitrateMbps: Double = 20.0
        private set

    private var lastPingTimestampUs: Long = 0

    fun onFrameReceived(bytesLength: Int) {
        frameCount.incrementAndGet()
        byteCount.addAndGet(bytesLength.toLong())

        val now = SystemClock.elapsedRealtime()
        val elapsed = now - lastCalculationTime
        if (elapsed >= 1000) {
            val frames = frameCount.getAndSet(0)
            val bytes = byteCount.getAndSet(0L)
            currentFps = (frames * 1000.0) / elapsed
            currentBitrateMbps = (bytes * 8.0) / (elapsed * 1000.0)
            lastCalculationTime = now

            // Send stats to PC
            onSendPacket(Packet.createStats(currentFps, 8.0, currentLatencyMs))

            // Trigger next ping
            sendPing()
        }
    }

    fun sendPing() {
        lastPingTimestampUs = SystemClock.elapsedRealtimeNanos() / 1000L
        onSendPacket(Packet.createPing(lastPingTimestampUs))
    }

    fun onPongReceived(timestampUs: Long) {
        val nowUs = SystemClock.elapsedRealtimeNanos() / 1000L
        val rttUs = nowUs - timestampUs
        if (rttUs in 0..1_000_000) {
            currentLatencyMs = rttUs / 1000.0
        }
    }
}
