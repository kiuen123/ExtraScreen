package com.extrascreen.protocol

object ProtocolConstants {
    const val DEFAULT_PORT = 7070
    const val FIXED_WIDTH = 1920
    const val FIXED_HEIGHT = 1080
    const val FIXED_FPS = 60

    const val TYPE_HELLO: Byte = 0x01
    const val TYPE_SESSION_CONFIG: Byte = 0x02
    const val TYPE_VIDEO_CONFIG: Byte = 0x10
    const val TYPE_VIDEO_FRAME: Byte = 0x11
    const val TYPE_REQUEST_KEYFRAME: Byte = 0x12
    const val TYPE_FRAME_ACK: Byte = 0x13
    const val TYPE_AUDIO_FRAME: Byte = 0x15
    const val TYPE_TOUCH: Byte = 0x20
    const val TYPE_PING: Byte = 0x30
    const val TYPE_PONG: Byte = 0x31
    const val TYPE_STATS: Byte = 0x40
    const val TYPE_BYE: Byte = 0xFF.toByte()

    const val TOUCH_ACTION_DOWN: Byte = 0
    const val TOUCH_ACTION_MOVE: Byte = 1
    const val TOUCH_ACTION_UP: Byte = 2
    const val TOUCH_ACTION_CANCEL: Byte = 3

    const val CODEC_H264 = "h264"
    const val CODEC_H265 = "h265"
}
