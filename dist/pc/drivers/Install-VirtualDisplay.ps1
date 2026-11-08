# ExtraScreen - Script cài đặt Driver Màn hình ảo (Virtual Display Driver)
# Cần chạy với quyền Administrator (Run as Administrator)

Write-Host "=== ExtraScreen - Cài đặt Driver Màn hình ảo ===" -ForegroundColor Cyan

# Kiểm tra xem máy đã có adapter ảo chưa (Parsec VDD, Amyuni, IddCx...)
$existing = Get-CimInstance Win32_VideoController | Where-Object { 
    $_.Name -like "*Virtual*" -or $_.Name -like "*Parsec*" -or $_.Name -like "*IddCx*" 
}

if ($existing) {
    Write-Host "[OK] Đã phát hiện Driver Màn hình ảo trên máy:" -ForegroundColor Green
    $existing | ForEach-Object { Write-Host " - $($_.Name) (Driver: $($_.DriverVersion))" -ForegroundColor Green }
    Write-Host "Hệ thống đã sẵn sàng cho chế độ Màn hình mở rộng (Extend Display)!" -ForegroundColor Green
    exit 0
}

Write-Host "Chưa phát hiện driver màn hình ảo chuyên dụng." -ForegroundColor Yellow
Write-Host "Bạn có thể sử dụng giải pháp Indirect Display Driver (IddCx) mã nguồn mở hoặc Parsec VDD." -ForegroundColor White
Write-Host "Tham khảo hướng dẫn tại: https://github.com/ge9/IddSampleDriver hoặc cài đặt Parsec Virtual Display Driver." -ForegroundColor Gray
Write-Host ""
Write-Host "Lưu ý: Nếu không muốn cài đặt driver, bạn vẫn có thể sử dụng chế độ 'Màn hình phản chiếu (Mirror Display)' sẵn có trong ExtraScreen mà không cần bất kỳ driver nào!" -ForegroundColor Green
