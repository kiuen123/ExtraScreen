package com.extrascreen.ui

import android.content.Context
import android.content.Intent
import android.provider.Settings
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun OnboardingScreen(
    onContinueIfEnabled: () -> Unit
) {
    val context = LocalContext.current
    val scrollState = rememberScrollState()

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Slate900)
            .padding(24.dp)
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .verticalScroll(scrollState),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center
        ) {
            // Icon
            Text(
                text = "🔌 ⮀ 💻",
                fontSize = 32.sp,
                modifier = Modifier.padding(bottom = 12.dp)
            )

            // Header Title
            Text(
                text = "KÍCH HOẠT GỠ LỖI USB",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White,
                textAlign = TextAlign.Center
            )
            Text(
                text = "Để biến điện thoại thành màn hình phụ qua cáp, bạn chỉ cần bật tính năng Gỡ lỗi USB một lần duy nhất theo các bước sau:",
                fontSize = 13.sp,
                color = Slate400,
                textAlign = TextAlign.Center,
                modifier = Modifier.padding(top = 8.dp, bottom = 20.dp, start = 12.dp, end = 12.dp)
            )

            // Step 1
            StepCard(
                step = "1",
                title = "Mở Tùy chọn nhà phát triển",
                description = "Vào Cài đặt > Thông tin điện thoại > Chạm 7 lần liên tiếp vào dòng \"Số hiệu bản dựng\" (Build Number)."
            )

            Spacer(modifier = Modifier.height(10.dp))

            // Step 2
            StepCard(
                step = "2",
                title = "Bật Gỡ lỗi qua USB",
                description = "Vào Cài đặt > Hệ thống > Tùy chọn nhà phát triển > Gạt BẬT \"Gỡ lỗi qua USB\" (USB Debugging)."
            )

            Spacer(modifier = Modifier.height(10.dp))

            // Step 3
            StepCard(
                step = "3",
                title = "Cắm cáp USB và bấm Cho phép",
                description = "Cắm cáp nối với PC. Tích vào \"Luôn cho phép từ máy tính này\" và bấm Cho phép (OK) khi hộp thoại hiện lên."
            )

            Spacer(modifier = Modifier.height(20.dp))

            // Action Button
            Button(
                onClick = {
                    openDevSettings(context)
                },
                shape = RoundedCornerShape(8.dp),
                colors = ButtonDefaults.buttonColors(containerColor = RoyalBlue),
                modifier = Modifier
                    .fillMaxWidth(0.85f)
                    .height(48.dp)
            ) {
                Text(
                    text = "⚙️  MỞ CÀI ĐẶT NHÀ PHÁT TRIỂN NGAY",
                    fontSize = 14.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color.White
                )
            }

            Spacer(modifier = Modifier.height(14.dp))

            // Status Indicator
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.Center
            ) {
                Text(
                    text = "🟡 Đang chờ phát hiện Gỡ lỗi USB...",
                    fontSize = 12.sp,
                    color = AmberYellow
                )
            }
        }
    }
}

@Composable
private fun StepCard(step: String, title: String, description: String) {
    Card(
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(containerColor = Slate800),
        modifier = Modifier.fillMaxWidth(0.9f)
    ) {
        Row(
            modifier = Modifier.padding(14.dp),
            verticalAlignment = Alignment.Top
        ) {
            Surface(
                shape = RoundedCornerShape(12.dp),
                color = RoyalBlue,
                modifier = Modifier.size(24.dp)
            ) {
                Box(contentAlignment = Alignment.Center) {
                    Text(text = step, color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                }
            }

            Spacer(modifier = Modifier.width(12.dp))

            Column {
                Text(text = title, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, color = Color.White)
                Spacer(modifier = Modifier.height(2.dp))
                Text(text = description, fontSize = 12.sp, color = Slate400, lineHeight = 16.sp)
            }
        }
    }
}

private fun openDevSettings(context: Context) {
    try {
        val intent = Intent(Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS)
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
    } catch (e: Exception) {
        try {
            val fallback = Intent(Settings.ACTION_SETTINGS)
            fallback.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            context.startActivity(fallback)
        } catch (ex: Exception) {
            // Ignore
        }
    }
}
