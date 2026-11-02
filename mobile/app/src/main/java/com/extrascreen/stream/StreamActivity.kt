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
import com.extrascreen.connection.UsbClient
import com.extrascreen.decoder.MediaCodecDecoder
import com.extrascreen.protocol.Packet
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
    private var decoder: MediaCodecDecoder? = null
    private var activeSurface: Surface? = null
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
                    codecState.value = codec
                    val mime = if (codec.equals("h265", ignoreCase = true)) "video/hevc" else "video/avc"
                    activeSurface?.let { surf ->
                        decoder = MediaCodecDecoder(surf) {
                            usbClient.sendPacket(Packet.createRequestKeyframe())
                        }.apply {
                            configure(mime, width, height)
                        }
                    }
                    screenState.value = ScreenState.Streaming
                }
            }

            override fun onVideoConfig(configData: ByteArray) {
                runOnUiThread {
                    val mime = if (codecState.value.equals("h265", ignoreCase = true)) "video/hevc" else "video/avc"
                    activeSurface?.let { surf ->
                        decoder?.release()
                        decoder = MediaCodecDecoder(surf) {
                            usbClient.sendPacket(Packet.createRequestKeyframe())
                        }.apply {
                            configure(mime, 1920, 1080, configData)
                        }
                    }
                }
            }

            override fun onVideoFrame(frameData: ByteArray, isKeyframe: Boolean, timestampUs: Long) {
                decoder?.decodeFrame(frameData, timestampUs)
                statsCollector.onFrameReceived(frameData.size)
                runOnUiThread {
                    fpsState.doubleValue = statsCollector.currentFps
                    latencyState.doubleValue = statsCollector.currentLatencyMs
                    bitrateState.doubleValue = statsCollector.currentBitrateMbps
                }
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

    @Composable
    private fun StreamingView() {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
                .pointerInput(Unit) {
                    detectTapGestures(
                        onDoubleTap = {
                            showHud.value = !showHud.value
                        }
                    )
                }
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

    // SurfaceHolder.Callback
    override fun surfaceCreated(holder: SurfaceHolder) {
        activeSurface = holder.surface
    }

    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
        activeSurface = holder.surface
    }

    override fun surfaceDestroyed(holder: SurfaceHolder) {
        activeSurface = null
        decoder?.release()
        decoder = null
    }
}
