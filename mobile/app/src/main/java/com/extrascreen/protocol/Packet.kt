package com.extrascreen.protocol

import org.json.JSONArray
import org.json.JSONObject
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

data class Packet(
    val type: Byte,
    val timestampUs: Long,
    val payload: ByteArray
) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (javaClass != other?.javaClass) return false
        other as Packet
        if (type != other.type) return false
        if (timestampUs != other.timestampUs) return false
        return payload.contentEquals(other.payload)
    }

    override fun hashCode(): Int {
        var result = type.toInt()
        result = 31 * result + timestampUs.hashCode()
        result = 31 * result + payload.contentHashCode()
        return result
    }

    companion object {
        const val HEADER_SIZE = 13

        fun createHello(
            model: String,
            width: Int,
            height: Int,
            dpi: Int,
            codecs: List<String>,
            version: String = "1.0.0"
        ): Packet {
            val json = JSONObject().apply {
                put("model", model)
                put("width", width)
                put("height", height)
                put("dpi", dpi)
                put("codecs", JSONArray(codecs))
                put("version", version)
            }
            val bytes = json.toString().toByteArray(Charsets.UTF_8)
            return Packet(ProtocolConstants.TYPE_HELLO, 0L, bytes)
        }

        fun createPing(timestampUs: Long): Packet {
            val buf = ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN)
            buf.putLong(timestampUs)
            return Packet(ProtocolConstants.TYPE_PING, timestampUs, buf.array())
        }

        fun createPong(timestampUs: Long): Packet {
            val buf = ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN)
            buf.putLong(timestampUs)
            return Packet(ProtocolConstants.TYPE_PONG, timestampUs, buf.array())
        }

        fun createRequestKeyframe(): Packet {
            return Packet(ProtocolConstants.TYPE_REQUEST_KEYFRAME, 0L, ByteArray(0))
        }

        fun createStats(fps: Double, decodeMs: Double, rttMs: Double): Packet {
            val json = JSONObject().apply {
                put("fps", fps)
                put("decodeMs", decodeMs)
                put("rttMs", rttMs)
            }
            val bytes = json.toString().toByteArray(Charsets.UTF_8)
            return Packet(ProtocolConstants.TYPE_STATS, 0L, bytes)
        }

        fun createBye(reason: String): Packet {
            val json = JSONObject().apply {
                put("reason", reason)
            }
            val bytes = json.toString().toByteArray(Charsets.UTF_8)
            return Packet(ProtocolConstants.TYPE_BYE, 0L, bytes)
        }

        fun write(stream: OutputStream, packet: Packet) {
            val header = ByteBuffer.allocate(HEADER_SIZE).order(ByteOrder.LITTLE_ENDIAN)
            header.put(packet.type)
            header.putInt(packet.payload.size)
            header.putLong(packet.timestampUs)

            stream.write(header.array())
            if (packet.payload.isNotEmpty()) {
                stream.write(packet.payload)
            }
            stream.flush()
        }

        fun read(stream: InputStream): Packet? {
            val headerBytes = ByteArray(HEADER_SIZE)
            var totalRead = 0
            while (totalRead < HEADER_SIZE) {
                val read = stream.read(headerBytes, totalRead, HEADER_SIZE - totalRead)
                if (read == -1) return null
                totalRead += read
            }

            val headerBuf = ByteBuffer.wrap(headerBytes).order(ByteOrder.LITTLE_ENDIAN)
            val type = headerBuf.get()
            val length = headerBuf.int
            val timestampUs = headerBuf.long

            if (length < 0 || length > 100 * 1024 * 1024) {
                return null
            }

            val payload = ByteArray(length)
            totalRead = 0
            while (totalRead < length) {
                val read = stream.read(payload, totalRead, length - totalRead)
                if (read == -1) return null
                totalRead += read
            }

            return Packet(type, timestampUs, payload)
        }
    }

    fun asJsonObject(): JSONObject? {
        return try {
            JSONObject(String(payload, Charsets.UTF_8))
        } catch (e: Exception) {
            null
        }
    }
}
