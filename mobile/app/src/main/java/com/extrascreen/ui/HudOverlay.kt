package com.extrascreen.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun HudOverlay(
    fps: Double,
    latencyMs: Double,
    bitrateMbps: Double,
    codec: String,
    onRequestKeyframe: () -> Unit,
    onDisconnect: () -> Unit,
    onHide: () -> Unit,
    modifier: Modifier = Modifier
) {
    val fpsColor = when {
        fps >= 58.0 -> EmeraldGreen
        fps >= 45.0 -> AmberYellow
        else -> CrimsonRed
    }

    Box(
        modifier = modifier
            .padding(16.dp)
            .width(260.dp)
            .background(Color(0xCC0F172A), shape = RoundedCornerShape(12.dp))
            .border(1.dp, Color(0x66334155), shape = RoundedCornerShape(12.dp))
            .padding(14.dp)
    ) {
        Column {
            // Stats Header
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "${String.format("%.1f", fps)} FPS",
                    fontWeight = FontWeight.Bold,
                    fontSize = 14.sp,
                    color = fpsColor
                )
                Text(
                    text = "⚡ ${String.format("%.0f", latencyMs)} ms",
                    fontWeight = FontWeight.SemiBold,
                    fontSize = 13.sp,
                    color = CyanBlue
                )
            }

            Spacer(modifier = Modifier.height(4.dp))

            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "📶 ${String.format("%.1f", bitrateMbps)} Mbps",
                    fontSize = 12.sp,
                    color = AmberYellow
                )
                Text(
                    text = "🎬 ${codec.uppercase()}",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Medium,
                    color = Slate300
                )
            }

            Spacer(modifier = Modifier.height(12.dp))

            // Action Buttons
            Button(
                onClick = onRequestKeyframe,
                shape = RoundedCornerShape(6.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Slate700),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(34.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Text(text = "🔄 Khung hình IDR", fontSize = 12.sp, color = Color.White)
            }

            Spacer(modifier = Modifier.height(6.dp))

            Button(
                onClick = onDisconnect,
                shape = RoundedCornerShape(6.dp),
                colors = ButtonDefaults.buttonColors(containerColor = CrimsonRed),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(34.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Text(text = "🔌 Ngắt kết nối", fontSize = 12.sp, color = Color.White)
            }

            Spacer(modifier = Modifier.height(6.dp))

            Button(
                onClick = onHide,
                shape = RoundedCornerShape(6.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Color.Transparent),
                modifier = Modifier
                    .fillMaxWidth()
                    .height(28.dp),
                contentPadding = PaddingValues(0.dp)
            ) {
                Text(text = "👁️ Ẩn thông số (Chạm 2 lần để bật)", fontSize = 11.sp, color = Slate400)
            }
        }
    }
}
