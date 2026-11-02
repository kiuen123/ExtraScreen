package com.extrascreen.ui

import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun WaitingScreen(
    hasH265: Boolean = true
) {
    val infiniteTransition = rememberInfiniteTransition(label = "pulse")
    val pulseScale by infiniteTransition.animateFloat(
        initialValue = 0.95f,
        targetValue = 1.08f,
        animationSpec = infiniteRepeatable(
            animation = tween(1000, easing = FastOutSlowInEasing),
            repeatMode = RepeatMode.Reverse
        ),
        label = "pulseScale"
    )

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Slate900)
            .padding(24.dp),
        contentAlignment = Alignment.Center
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            // Pulse Icon
            Box(
                modifier = Modifier
                    .scale(pulseScale)
                    .size(72.dp)
                    .background(Slate800, shape = RoundedCornerShape(36.dp)),
                contentAlignment = Alignment.Center
            ) {
                Text(text = "🔌", fontSize = 34.sp)
            }

            Spacer(modifier = Modifier.height(18.dp))

            Text(
                text = "SẴN SÀNG KẾT NỐI",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White
            )

            Text(
                text = "Cắm cáp USB nối điện thoại với máy tính PC\nMàn hình phụ sẽ tự động xuất hiện trong vài giây",
                fontSize = 13.sp,
                color = Slate400,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp, bottom = 20.dp)
            )

            // Hardware Specs Card
            Card(
                shape = RoundedCornerShape(10.dp),
                colors = CardDefaults.cardColors(containerColor = Slate800),
                modifier = Modifier.fillMaxWidth(0.85f)
            ) {
                Column(
                    modifier = Modifier.padding(16.dp)
                ) {
                    Text(
                        text = "Thông số thiết bị:",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.SemiBold,
                        color = Color.White
                    )

                    Spacer(modifier = Modifier.height(8.dp))

                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(text = "• Phần cứng giải mã: ", fontSize = 12.sp, color = Slate400)
                        Text(text = "H.264 ✅  ", fontSize = 12.sp, color = EmeraldGreen, fontWeight = FontWeight.Bold)
                        if (hasH265) {
                            Text(text = "H.265 ✅", fontSize = 12.sp, color = EmeraldGreen, fontWeight = FontWeight.Bold)
                        }
                    }

                    Spacer(modifier = Modifier.height(4.dp))

                    Text(
                        text = "• Tần số quét đề xuất: 60 Hz / 120 Hz (Chống giật judder)",
                        fontSize = 12.sp,
                        color = Slate400
                    )

                    Spacer(modifier = Modifier.height(4.dp))

                    Text(
                        text = "• Cổng USB Socket: 127.0.0.1:7070 (adb reverse)",
                        fontSize = 12.sp,
                        color = Slate400
                    )
                }
            }

            Spacer(modifier = Modifier.height(24.dp))

            // Animated searching indicator
            Row(
                verticalAlignment = Alignment.CenterVertically
            ) {
                CircularProgressIndicator(
                    modifier = Modifier.size(16.dp),
                    color = RoyalBlue,
                    strokeWidth = 2.dp
                )
                Spacer(modifier = Modifier.width(10.dp))
                Text(
                    text = "Đang dò tìm tín hiệu PC qua cáp USB...",
                    fontSize = 12.sp,
                    color = Slate300
                )
            }
        }
    }
}
