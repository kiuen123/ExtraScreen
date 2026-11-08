package com.extrascreen.stream

import android.content.pm.ActivityInfo
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.view.*
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.lifecycleScope
import com.extrascreen.audio.AudioPlayer
import com.extrascreen.connection.UsbClient
import com.extrascreen.decoder.MediaCodecDecoder
import com.extrascreen.protocol.Packet
import com.extrascreen.protocol.ProtocolConstants
import com.extrascreen.stats.StatsCollector
import com.extrascreen.ui.ExtraScreenTheme
import com.extrascreen.ui.HudOverlay
import com.extrascreen.ui.OnboardingScreen
import com.extrascreen.ui.WaitingScreen
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

enum class ScreenState {
    Onboarding,
    Waiting,
    Streaming
}

class StreamActivity : ComponentActivity(), SurfaceHolder.Callback {

    private lateinit var usbClient: UsbClient
    private lateinit var statsCollector: StatsCollector
    private val audioPlayer = AudioPlayer()
    private var decoder: MediaCodecDecoder? = null
    private var activeSurface: Surface? = null
    private var pendingVideoConfig: ByteArray? = null
    private var sessionWidth: Int = 1920
    private var sessionHeight: Int = 1080
    private var connectJob: Job? = null

    // Compose observable state
    private val screenState = mutableStateOf(ScreenState.Waiting)
    private val showHud = mutableStateOf(true)
    private val fpsState = mutableDoubleStateOf(60.0)
    private val latencyState = mutableDoubleStateOf(38.0)
    private val bitrateState = mutableDoubleStateOf(20.0)
    private val codecState = mutableStateOf("h264")

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Lock to landscape & keep screen awake
        requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Setup Immersive Sticky Fullscreen
        enableImmersiveMode()

        // Optimize Display Refresh Rate (prefer 60Hz or 120Hz)
        optimizeRefreshRate()

        // Init Stats Collector
        statsCollector = StatsCollector { packet ->
            usbClient.sendPacket(packet)
        }

        // Init USB Client
        usbClient = UsbClient(this, object : UsbClient.Listener {
            override fun onConnected() {
                // Wait for SESSION_CONFIG from PC
            }

            override fun onSessionConfig(width: Int, height: Int, fps: Int, codec: String, bitrate: Int) {
                runOnUiThread {
                    sessionWidth = width
                    sessionHeight = height
                    codecState.value = codec
                    audioPlayer.initialize()
                    screenState.value = ScreenState.Streaming
                    ensureDecoderInitialized()
                }
            }

            override fun onVideoConfig(configData: ByteArray) {
                runOnUiThread {
                    pendingVideoConfig = configData
                    screenState.value = ScreenState.Streaming
                    ensureDecoderInitialized(forceReconfigure = true)
                }
            }

            override fun onVideoFrame(frameData: ByteArray, isKeyframe: Boolean, timestampUs: Long) {
                if (decoder == null && isKeyframe) {
                    runOnUiThread {
                        if (pendingVideoConfig == null) {
                            pendingVideoConfig = frameData
                        }
                        ensureDecoderInitialized()
                        decoder?.decodeFrame(frameData, timestampUs)
                    }
                } else {
                    decoder?.decodeFrame(frameData, timestampUs)
                }
                statsCollector.onFrameReceived(frameData.size)
                runOnUiThread {
                    fpsState.doubleValue = statsCollector.currentFps
                    latencyState.doubleValue = statsCollector.currentLatencyMs
                    bitrateState.doubleValue = statsCollector.currentBitrateMbps
                }
            }

            override fun onAudioFrame(audioData: ByteArray, timestampUs: Long) {
                audioPlayer.writePcm(audioData)
            }

            override fun onPong(timestampUs: Long) {
                statsCollector.onPongReceived(timestampUs)
                runOnUiThread {
                    latencyState.doubleValue = statsCollector.currentLatencyMs
                }
            }

            override fun onDisconnected() {
                runOnUiThread {
                    decoder?.release()
                    decoder = null
                    audioPlayer.release()
                    screenState.value = ScreenState.Waiting
                }
            }
        })

        // Check if USB debugging is on
        checkUsbDebugging()

        // Set UI
        setContent {
            ExtraScreenTheme {
                MainContent()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        enableImmersiveMode()
        checkUsbDebugging()
        startConnectionLoop()
    }

    override fun onPause() {
        super.onPause()
        stopConnectionLoop()
    }

    override fun onDestroy() {
        super.onDestroy()
        decoder?.release()
        decoder = null
        audioPlayer.release()
        usbClient.close()
    }

    private fun checkUsbDebugging() {
        val adbEnabled = Settings.Global.getInt(contentResolver, Settings.Global.ADB_ENABLED, 0)
        if (adbEnabled == 0 && screenState.value != ScreenState.Streaming) {
            screenState.value = ScreenState.Onboarding
        } else if (screenState.value == ScreenState.Onboarding && adbEnabled == 1) {
            screenState.value = ScreenState.Waiting
        }
    }

    private fun startConnectionLoop() {
        connectJob?.cancel()
        connectJob = lifecycleScope.launch {
            usbClient.connectAndRun()
        }
    }

    private fun stopConnectionLoop() {
        connectJob?.cancel()
        connectJob = null
    }

    private fun enableImmersiveMode() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        val controller = WindowInsetsControllerCompat(window, window.decorView)
        controller.hide(WindowInsetsCompat.Type.systemBars())
        controller.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
    }

    private fun optimizeRefreshRate() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                val display = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                    display
                } else {
                    @Suppress("DEPRECATION")
                    windowManager.defaultDisplay
                }
                val modes = display?.supportedModes ?: return
                // Find 60Hz or 120Hz mode
                val targetMode = modes.firstOrNull { Math.abs(it.refreshRate - 60f) < 0.5f }
                    ?: modes.firstOrNull { Math.abs(it.refreshRate - 120f) < 0.5f }

                targetMode?.let {
                    val params = window.attributes
                    params.preferredDisplayModeId = it.modeId
                    window.attributes = params
                }
            } catch (e: Exception) {
                // Ignore
            }
        }
    }

    @Composable
    private fun MainContent() {
        when (screenState.value) {
            ScreenState.Onboarding -> {
                OnboardingScreen(
                    onContinueIfEnabled = { checkUsbDebugging() }
                )
            }
            ScreenState.Waiting -> {
                WaitingScreen(
                    hasH265 = usbClient.getSupportedCodecs().contains("h265")
                )
            }
            ScreenState.Streaming -> {
                StreamingView()
            }
        }
    }

    private var lastTwoFingerTapTime = 0L

    private fun handleSurfaceTouch(view: View, event: MotionEvent): Boolean {
        val width = view.width.toFloat()
        val height = view.height.toFloat()
        if (width <= 0f || height <= 0f) return false

        // Double-tap bằng 2 ngón tay để bật/tắt HUD
        if (event.pointerCount >= 2 && event.actionMasked == MotionEvent.ACTION_POINTER_DOWN) {
            val now = System.currentTimeMillis()
            if (now - lastTwoFingerTapTime < 500) {
                showHud.value = !showHud.value
                lastTwoFingerTapTime = 0L
                return true
            }
            lastTwoFingerTapTime = now
        }

        val action = when (event.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> ProtocolConstants.TOUCH_ACTION_DOWN
            MotionEvent.ACTION_MOVE -> ProtocolConstants.TOUCH_ACTION_MOVE
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> ProtocolConstants.TOUCH_ACTION_UP
            MotionEvent.ACTION_CANCEL -> ProtocolConstants.TOUCH_ACTION_CANCEL
            else -> return false
        }

        val index = event.actionIndex
        if (event.actionMasked == MotionEvent.ACTION_MOVE) {
            for (i in 0 until event.pointerCount) {
                val pid = event.getPointerId(i)
                val normX = (event.getX(i) / width).coerceIn(0f, 1f)
                val normY = (event.getY(i) / height).coerceIn(0f, 1f)
                val pressure = event.getPressure(i).coerceIn(0f, 1f)
                usbClient.sendTouch(action, pid, normX, normY, pressure)
            }
        } else {
            val pid = event.getPointerId(index)
            val normX = (event.getX(index) / width).coerceIn(0f, 1f)
            val normY = (event.getY(index) / height).coerceIn(0f, 1f)
            val pressure = event.getPressure(index).coerceIn(0f, 1f)
            usbClient.sendTouch(action, pid, normX, normY, pressure)
        }

        return true
    }

    @Composable
    private fun StreamingView() {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
        ) {
            // AspectRatio 16:9 container with black letterboxing
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .aspectRatio(16f / 9f)
                    .align(Alignment.Center)
            ) {
                AndroidView(
                    factory = { ctx ->
                        SurfaceView(ctx).apply {
                            holder.addCallback(this@StreamActivity)
                            setOnTouchListener { v, event ->
                                handleSurfaceTouch(v, event)
                            }
                        }
                    },
                    modifier = Modifier.fillMaxSize()
                )
            }

            // HUD Overlay (Glassmorphism top right)
            if (showHud.value) {
                HudOverlay(
                    fps = fpsState.doubleValue,
                    latencyMs = latencyState.doubleValue,
                    bitrateMbps = bitrateState.doubleValue,
                    codec = codecState.value,
                    onRequestKeyframe = {
                        usbClient.sendPacket(Packet.createRequestKeyframe())
                    },
                    onDisconnect = {
                        usbClient.close()
                        decoder?.release()
                        decoder = null
                        screenState.value = ScreenState.Waiting
                        startConnectionLoop()
                    },
                    onHide = {
                        showHud.value = false
                    },
                    modifier = Modifier.align(Alignment.TopEnd)
                )
            }
        }
    }

    private fun ensureDecoderInitialized(forceReconfigure: Boolean = false) {
        val surf = activeSurface ?: return
        if (!surf.isValid) return
        val cfg = pendingVideoConfig ?: return
        if (decoder != null && !forceReconfigure) return

        val mime = if (codecState.value.equals("h265", ignoreCase = true)) "video/hevc" else "video/avc"
        decoder?.release()
        decoder = MediaCodecDecoder(
            surface = surf,
            onRequestKeyframe = { usbClient.sendPacket(Packet.createRequestKeyframe()) },
            onFrameDecoded = { decodeMs -> statsCollector.onFrameDecoded(decodeMs) }
        ).apply {
            configure(mime, sessionWidth, sessionHeight, cfg)
        }
    }

    // SurfaceHolder.Callback
    override fun surfaceCreated(holder: SurfaceHolder) {
        activeSurface = holder.surface
        ensureDecoderInitialized(forceReconfigure = true)
        usbClient.sendPacket(Packet.createRequestKeyframe())
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        activeSurface = holder.surface
        if (decoder == null) {
            ensureDecoderInitialized(forceReconfigure = true)
            usbClient.sendPacket(Packet.createRequestKeyframe())
        }
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        activeSurface = null
        decoder?.release()
        decoder = null
    }
}
