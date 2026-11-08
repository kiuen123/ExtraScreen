package com.extrascreen.decoder

/**
 * Bộ phân tích NAL Unit Annex-B (H.264 / H.265)
 * Trích xuất và mở rộng từ cấu trúc R2.a() / R2.b() / R2.d() của Spacedesk APK (qua REA).
 */
object NalUnitParser {

    data class H264Csd(
        val sps: ByteArray?,
        val pps: ByteArray?
    )

    data class NalRange(
        val startOffset: Int,
        val payloadOffset: Int,
        val endOffset: Int
    )

    /**
     * Quét toàn bộ các NAL unit Annex-B (bắt đầu bằng 00 00 01 hoặc 00 00 00 01) trong mảng byte.
     */
    fun findNalUnits(data: ByteArray): List<NalRange> {
        val list = ArrayList<NalRange>()
        val len = data.size
        var i = 0
        var currentStart = -1
        var currentPayload = -1

        while (i <= len - 3) {
            if (data[i].toInt() == 0 && data[i + 1].toInt() == 0) {
                var prefixLen = 0
                if (data[i + 2].toInt() == 1) {
                    prefixLen = 3
                } else if (i + 3 < len && data[i + 2].toInt() == 0 && data[i + 3].toInt() == 1) {
                    prefixLen = 4
                }

                if (prefixLen > 0) {
                    if (currentStart >= 0 && currentPayload < i) {
                        list.add(NalRange(currentStart, currentPayload, i))
                    }
                    currentStart = i
                    currentPayload = i + prefixLen
                    i += prefixLen
                    continue
                }
            }
            i++
        }

        if (currentStart >= 0 && currentPayload < len) {
            list.add(NalRange(currentStart, currentPayload, len))
        }

        return list
    }

    /**
     * Tách riêng SPS (NAL type 7 -> csd-0) và PPS (NAL type 8 -> csd-1) cho H.264
     * theo mẫu R2.a(byte[]) của Spacedesk.
     */
    fun extractH264Csd(data: ByteArray?): H264Csd {
        if (data == null || data.isEmpty()) {
            return H264Csd(null, null)
        }
        var sps: ByteArray? = null
        var pps: ByteArray? = null

        for (range in findNalUnits(data)) {
            if (range.payloadOffset < range.endOffset) {
                val nalType = data[range.payloadOffset].toInt() and 0x1F
                if (nalType == 7) {
                    sps = data.copyOfRange(range.startOffset, range.endOffset)
                } else if (nalType == 8) {
                    pps = data.copyOfRange(range.startOffset, range.endOffset)
                }
            }
        }
        return H264Csd(sps, pps)
    }

    /**
     * Tìm VCL slice đầu tiên (NAL type 1 hoặc 5 đối với H.264) theo mẫu R2.d(byte[]) của Spacedesk.
     */
    fun extractFirstH264VclSlice(data: ByteArray?): ByteArray? {
        if (data == null || data.isEmpty()) return null
        val units = findNalUnits(data)
        if (units.isEmpty()) return data

        for (range in units) {
            if (range.payloadOffset < range.endOffset) {
                val nalType = data[range.payloadOffset].toInt() and 0x1F
                if (nalType == 1 || nalType == 5) {
                    return data.copyOfRange(range.startOffset, range.endOffset)
                }
            }
        }
        return data
    }
}
