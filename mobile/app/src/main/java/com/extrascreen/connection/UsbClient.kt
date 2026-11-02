package com.extrascreen.connection

import android.content.Context
import android.media.MediaCodecList
import android.os.Build
import com.extrascreen.protocol.Packet
import com.extrascreen.protocol.ProtocolConstants
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.LinkedBlockingQueue

class UsbClient(
    private val context: Context,
    private val listener: Listener
) {
    interface Listener {
        fun onConnected()
        fun onSessionConfig(width: Int, height: Int, fps: Int, codec: String, bitrate: Int)
        fun onVideoConfig(configData: ByteArray)
        fun onVideoFrame(frameData: ByteArray, isKeyframe: Boolean, timestampUs: Long)
        fun onPong(timestampUs: Long)
        fun onDisconnected()
    }

    private var socket: Socket? = null
    private var inputStream: InputStream? = null
    private var outputStream: OutputStream? = null
    private val sendQueue = LinkedBlockingQueue<Packet>()
    @Volatile
    private var isRunning = false

    fun getSupportedCodecs(): List<String> {
        val list = mutableListOf("h264")
        try {
            val codecList = MediaCodecList(MediaCodecList.REGULAR_CODECS)
            for (info in codecList.codecInfos) {
                if (info.isEncoder) continue
                for (type in info.supportedTypes) {
                    if (type.equals("video/hevc", ignoreCase = true)) {
                        if (!list.contains("h265")) list.add("h265")
                    }
                }
            }
        } catch (e: Exception) {
            // Ignore
        }
        return list
    }

    suspend fun connectAndRun() = withContext(Dispatchers.IO) {
        isRunning = true
        while (isRunning) {
            try {
                val s = Socket()
                s.tcpNoDelay = true
                s.sendBufferSize = 1024 * 1024
                s.receiveBufferSize = 1024 * 1024
                s.connect(InetSocketAddress("127.0.0.1", ProtocolConstants.DEFAULT_PORT), 1500)

                socket = s
                inputStream = s.getInputStream()
                outputStream = s.getOutputStream()

                listener.onConnected()

                // Send HELLO
                val displayMetrics = context.resources.displayMetrics
                val helloPacket = Packet.createHello(
                    model = "${Build.MANUFACTURER} ${Build.MODEL}",
                    width = displayMetrics.widthPixels,
                    height = displayMetrics.heightPixels,
                    dpi = displayMetrics.densityDpi,
                    codecs = getSupportedCodecs()
                )
                Packet.write(outputStream!!, helloPacket)

                // Start send thread
                val sendThread = Thread {
                    try {
                        while (isRunning && socket?.isConnected == true) {
                            val pkt = sendQueue.take()
                            outputStream?.let { Packet.write(it, pkt) }
                        }
                    } catch (e: Exception) {
                        // Send thread interrupted
                    }
                }
                sendThread.start()

                // Main receive loop
                while (isRunning) {
                    val pkt = Packet.read(inputStream!!) ?: break
                    handlePacket(pkt)
                }

                sendThread.interrupt()
            } catch (e: Exception) {
                // Connection failed or lost, retry after 500ms
            } finally {
                closeConnection()
                listener.onDisconnected()
            }

            if (isRunning) {
                kotlinx.coroutines.delay(500)
            }
        }
    }

    private fun handlePacket(packet: Packet) {
        when (packet.type) {
            ProtocolConstants.TYPE_SESSION_CONFIG -> {
                val json = packet.asJsonObject() ?: return
                val width = json.optInt("width", 1920)
                val height = json.optInt("height", 1080)
                val fps = json.optInt("fps", 60)
                val codec = json.optString("codec", "h264")
                val bitrate = json.optInt("bitrate", 20_000_000)
                listener.onSessionConfig(width, height, fps, codec, bitrate)
            }
            ProtocolConstants.TYPE_VIDEO_CONFIG -> {
                listener.onVideoConfig(packet.payload)
            }
            ProtocolConstants.TYPE_VIDEO_FRAME -> {
                if (packet.payload.isNotEmpty()) {
                    val isKey = packet.payload[0].toInt() == 1
                    val nals = packet.payload.copyOfRange(1, packet.payload.size)
                    listener.onVideoFrame(nals, isKey, packet.timestampUs)
                }
            }
            ProtocolConstants.TYPE_PONG -> {
                listener.onPong(packet.timestampUs)
            }
        }
    }

    fun sendPacket(packet: Packet) {
        if (isRunning) {
            sendQueue.offer(packet)
        }
    }

    fun close() {
        isRunning = false
        closeConnection()
    }

    private fun closeConnection() {
        try {
            socket?.close()
        } catch (e: Exception) {
            // Ignore
        }
        socket = null
        inputStream = null
        outputStream = null
        sendQueue.clear()
    }
}
